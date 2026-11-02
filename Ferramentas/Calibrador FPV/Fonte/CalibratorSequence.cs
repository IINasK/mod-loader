using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

public sealed class ControlSample
{
    public double Throttle, Yaw, Roll, Pitch;
    public ushort Buttons;
    public byte LeftTrigger, RightTrigger;
    public bool ContinuePressed;

    public ControlSample Copy()
    {
        return (ControlSample)MemberwiseClone();
    }
}

public sealed class CalibratorSettings
{
    public bool VerticalTest = true;
    public double ClimbSeconds = 0.35, PulseSeconds = 1.5,
        BaselineSeconds = 0.5, VerticalTimeout = 20;
    public int Repetitions = 1;
    public bool AllRates = true;
    public bool ControlledStart = false;
    public bool RequireFocus = true;
    public double RotationThrottle = 0.35;
    public bool ThrottleTest = false;
    public double ThrottlePulseSeconds = 1;
}

// Pure state machine: the caller owns the clock, focus detection, recording and R3 handling.
public sealed class CalibratorSequence
{
    private const double CountdownSeconds = 3;
    private const double RecoverySeconds = 0.5;
    private const double PreparationSeconds = 1;
    private const ushort RightThumbButton = 0x0080;
    private static long nextInstance;
    private readonly long instance = Interlocked.Increment(ref nextInstance);
    private readonly CalibratorSettings settings;
    private readonly List<Step> steps = new List<Step>();
    private int position, run;
    private double lastNow, countdownEnd, prepareStart, runningStart, heldThrottle;
    private bool started;

    private sealed class Step
    {
        public string Axis;
        public int Percent, Repetition;
    }

    public string State { get; private set; }
    public string Status { get; private set; }
    public string ClipName { get; private set; }
    public bool Recording { get; private set; }
    public bool Complete { get; private set; }
    // StepIndex is one-based after Start; it stays on the final step at completion.
    public int StepIndex { get { return started ? position + 1 : 0; } }
    public int StepCount { get { return steps.Count; } }

    public CalibratorSequence(CalibratorSettings source)
    {
        if (source == null) throw new ArgumentNullException("source");
        CheckRange(source.ClimbSeconds, 0, 1.5, "ClimbSeconds");
        CheckRange(source.PulseSeconds, 0.25, 4, "PulseSeconds");
        CheckRange(source.BaselineSeconds, 0.1, 2, "BaselineSeconds");
        CheckRange(source.VerticalTimeout, 2, 30, "VerticalTimeout");
        CheckRange(source.RotationThrottle, 0, 1, "RotationThrottle");
        CheckRange(source.ThrottlePulseSeconds, 0.25, 2, "ThrottlePulseSeconds");
        if (source.ThrottleTest && (!source.ControlledStart || source.ThrottlePulseSeconds >= source.VerticalTimeout))
            throw new ArgumentException("O teste de aceleração exige preparação controlada e tempo para a queda.");
        if (source.Repetitions < 1 || source.Repetitions > 3)
            throw new ArgumentOutOfRangeException("Repetitions", "Use de 1 a 3 repetições.");
        settings = new CalibratorSettings {
            VerticalTest = source.VerticalTest, ClimbSeconds = source.ClimbSeconds,
            PulseSeconds = source.PulseSeconds, BaselineSeconds = source.BaselineSeconds,
            VerticalTimeout = source.VerticalTimeout, Repetitions = source.Repetitions,
            AllRates = source.AllRates, ControlledStart = source.ControlledStart, RequireFocus = source.RequireFocus,
            RotationThrottle = source.RotationThrottle, ThrottleTest = source.ThrottleTest,
            ThrottlePulseSeconds = source.ThrottlePulseSeconds
        };
        if (settings.ThrottleTest)
        {
            for (int percent = 25; percent <= 75; percent += 25)
                for (int repetition = 1; repetition <= settings.Repetitions; repetition++)
                    steps.Add(new Step { Axis = "throttle", Percent = percent, Repetition = repetition });
        }
        else
        {
            if (settings.VerticalTest)
                steps.Add(new Step { Axis = "vertical", Percent = 100, Repetition = 1 });
            foreach (string axis in new string[] { "yaw", "roll", "pitch" })
                for (int percent = settings.AllRates ? 25 : 100; percent <= 100; percent += 25)
                    for (int repetition = 1; repetition <= settings.Repetitions; repetition++)
                        steps.Add(new Step { Axis = axis, Percent = percent, Repetition = repetition });
        }
        State = "Idle";
        Status = "Pronto para iniciar. Reposicionar, ARM manual e estabilizar antes de confirmar.";
        ClipName = "";
    }

    public void Start(double now)
    {
        if (!Finite(now)) throw new ArgumentOutOfRangeException("now");
        started = true;
        run++;
        position = 0;
        lastNow = now;
        Complete = false;
        Recording = false;
        WaitForConfirmation();
    }

    public void Confirm(double now, ControlSample physical)
    {
        if (!started || Complete || State == "Aborted") return;
        if (!AcceptTime(now)) return;
        if (physical == null) { Abort("Controle físico indisponível."); return; }
        if (State == "Waiting")
        {
            heldThrottle = settings.ControlledStart ? settings.RotationThrottle : Clamp(physical.Throttle, 0, 1);
            if (settings.ControlledStart)
            {
                prepareStart = now;
                State = "Preparing";
                Recording = false;
                Status = settings.RequireFocus ? "Preparação: potência zero por 1 s; manter o jogo em foco." : "Preparação: potência zero por 1 s; início automático em seguida.";
            }
            else
            {
                countdownEnd = now + CountdownSeconds;
                State = "Countdown";
                Status = "Preparação: focar o jogo em 3 segundos. ARM permanece manual.";
            }
        }
        else if (State == "RunningVertical" || State == "RunningThrottle")
        {
            double elapsed = now - runningStart;
            if (elapsed >= settings.VerticalTimeout)
                Abort("Tempo limite da queda. Reposicionar e reiniciar manualmente.");
            else if (elapsed >= (settings.ThrottleTest ? settings.ThrottlePulseSeconds : settings.ClimbSeconds))
                FinishStep();
        }
    }

    public ControlSample Tick(double now, ControlSample physical, bool foregroundOk)
    {
        ControlSample output = settings.ControlledStart || physical == null ? new ControlSample() : physical.Copy();
        if (!started || Complete || State == "Aborted") return output;
        if (!AcceptTime(now)) return output;
        if (physical == null) { Abort("Controle físico indisponível."); return output; }

        if (State == "Preparing")
        {
            if (settings.RequireFocus && !foregroundOk) { Abort("Jogo sem foco durante a preparação."); return output; }
            double preparationElapsed = now - prepareStart;
            if (preparationElapsed < PreparationSeconds)
            {
                Status = "Preparação: potência zero por 1 s; eixos neutros.";
                return output;
            }
            runningStart = now;
            State = settings.ThrottleTest ? "RunningThrottle" : steps[position].Axis == "vertical" ? "RunningVertical" : "RunningRotation";
            Recording = true;
        }
        if (State == "Countdown")
        {
            if (now < countdownEnd)
            {
                Status = "Preparação: focar o jogo em " +
                    Math.Ceiling(countdownEnd - now).ToString(CultureInfo.InvariantCulture) + " s. ARM manual.";
                return output;
            }
            if (settings.RequireFocus && !foregroundOk) { Abort("Jogo sem foco ao terminar a contagem."); return output; }
            // Start on the actual first output tick so a delayed caller never skips the climb.
            runningStart = now;
            State = settings.ThrottleTest ? "RunningThrottle" : steps[position].Axis == "vertical" ? "RunningVertical" : "RunningRotation";
            Recording = true;
        }
        if (State != "RunningVertical" && State != "RunningRotation" && State != "RunningThrottle") return output;
        if (settings.RequireFocus && !foregroundOk) { Abort("Jogo perdeu o foco. Sequência interrompida."); return output; }

        double elapsed = now - runningStart;
        Step step = steps[position];
        if ((State == "RunningVertical" || State == "RunningThrottle") && elapsed >= settings.VerticalTimeout)
        {
            Abort("Tempo limite da queda. Reposicionar e reiniciar manualmente.");
            return output;
        }
        if (State == "RunningThrottle")
        {
            // No full-power lift first: it would contaminate the throttle response measurement.
            output.Throttle = elapsed < settings.ThrottlePulseSeconds ? step.Percent / 100.0 : 0;
            Status = elapsed < settings.ThrottlePulseSeconds ?
                "Gravando: aceleração " + step.Percent + "% por " + settings.ThrottlePulseSeconds.ToString("0.##", CultureInfo.CurrentCulture) + " s; eixos neutros." :
                "Gravando: potência zero. F10 após tocar o chão; se não decolou, confirme agora. Depois resete a posição.";
            return output;
        }
        double pulseStart = settings.ClimbSeconds + settings.BaselineSeconds;
        double pulseEnd = pulseStart + settings.PulseSeconds;
        if (State == "RunningRotation" && elapsed >= pulseEnd + RecoverySeconds)
        {
            FinishStep();
            return output;
        }

        output.Throttle = elapsed < settings.ClimbSeconds ? 1 :
            (State == "RunningVertical" ? 0 : heldThrottle);
        output.Yaw = output.Roll = output.Pitch = 0;
        output.Buttons = settings.ControlledStart ? (ushort)0 : (ushort)(physical.Buttons & ~RightThumbButton);
        output.ContinuePressed = false;
        if (State == "RunningVertical")
            Status = elapsed < settings.ClimbSeconds ? "Gravando: subida vertical a 100%." :
                "Gravando: aceleração cortada. Confirmar SOMENTE após o impacto observado.";
        else if (elapsed < settings.ClimbSeconds)
            Status = "Gravando: subida a 100%.";
        else if (elapsed < pulseStart)
            Status = settings.ControlledStart ? "Gravando: estabilizando na potência de rotação configurada." :
                "Gravando: estabilizando na aceleração capturada.";
        else if (elapsed < pulseEnd)
        {
            double value = step.Percent / 100.0;
            if (step.Axis == "yaw") output.Yaw = value;
            else if (step.Axis == "roll") output.Roll = value;
            else output.Pitch = value;
            Status = "Gravando: pulso positivo " + StepDescription() + ".";
        }
        else
            Status = "Gravando: eixo neutro após o pulso.";
        output.Throttle = Clamp(output.Throttle, 0, 1);
        output.Yaw = Clamp(output.Yaw, -1, 1);
        output.Roll = Clamp(output.Roll, -1, 1);
        output.Pitch = Clamp(output.Pitch, -1, 1);
        return output;
    }

    public void Abort(string reason)
    {
        Recording = false;
        Complete = false;
        State = "Aborted";
        Status = "Interrompido: " + (String.IsNullOrWhiteSpace(reason) ? "cancelamento manual." : reason);
    }

    private bool AcceptTime(double now)
    {
        if (!Finite(now) || now < lastNow)
        {
            Abort("Relógio inválido ou regressivo.");
            return false;
        }
        lastNow = now;
        return true;
    }

    private void FinishStep()
    {
        Recording = false;
        if (position + 1 == steps.Count)
        {
            State = "Complete";
            Complete = true;
            Status = settings.ControlledStart ? "Testes concluídos. Potência mantida em zero." :
                "Testes concluídos. Controle físico restaurado.";
        }
        else
        {
            position++;
            WaitForConfirmation();
        }
    }

    private void WaitForConfirmation()
    {
        State = "Waiting";
        Step step = steps[position];
        ClipName = step.Axis + "_" + step.Percent.ToString("000", CultureInfo.InvariantCulture) +
            "_rep" + step.Repetition.ToString("00", CultureInfo.InvariantCulture) +
            "_run" + run.ToString("0000", CultureInfo.InvariantCulture) +
            "_id" + instance.ToString(CultureInfo.InvariantCulture);
        Status = "Etapa " + StepIndex + "/" + StepCount + ": " + StepDescription() + ". " +
            (settings.ControlledStart ?
                "Nivelar perto do chão, ARM manual se preciso; zero por 1 s não detecta pouso. Confirmar com F10." :
                "Reposicionar, ARM manual e estabilizar; confirmar para contagem de 3 s.");
    }

    private string StepDescription()
    {
        Step step = steps[position];
        return (step.Axis == "throttle" ? "aceleração" : step.Axis) + " " + step.Percent + "% (repetição " + step.Repetition + ")";
    }

    private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    private static double Clamp(double value, double minimum, double maximum)
    {
        return Double.IsNaN(value) ? 0 : Math.Max(minimum, Math.Min(maximum, value));
    }
    private static void CheckRange(double value, double minimum, double maximum, string name)
    {
        if (!Finite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }

    // Returns the assertion count; throws if any deterministic check fails. No devices or I/O.
    public static int SelfTest()
    {
        int checks = 0;
        ControlSample physical = new ControlSample { Throttle = 0.42, Yaw = -0.7, Roll = 0.8,
            Pitch = -0.2, Buttons = 0xFFFF, LeftTrigger = 32, RightTrigger = 201, ContinuePressed = true };
        CalibratorSettings compact = new CalibratorSettings { VerticalTest = false, AllRates = false,
            ClimbSeconds = 0.5, BaselineSeconds = 0.5, PulseSeconds = 1.5 };
        CalibratorSequence sequence = new CalibratorSequence(compact);
        sequence.Start(0);
        ControlSample sample = sequence.Tick(10, physical, false);
        Assert(ref checks, sequence.State == "Waiting" && !sequence.Recording && sequence.StepCount == 3,
            "Start must wait without recording, regardless of R3.");
        Assert(ref checks, !Object.ReferenceEquals(sample, physical) && sample.Yaw == physical.Yaw &&
            sample.Buttons == physical.Buttons && sample.ContinuePressed, "Waiting must return a physical copy.");
        string firstClip = sequence.ClipName;
        sequence.Confirm(10, physical);
        sample = sequence.Tick(12.5, physical, false);
        Assert(ref checks, sequence.State == "Countdown" && !sequence.Recording && sample.Throttle == 0.42,
            "Countdown permits focus changes and does not record.");
        sample = sequence.Tick(13, physical, true);
        Assert(ref checks, sequence.State == "RunningRotation" && sequence.Recording && sample.Throttle == 1 &&
            sample.Yaw == 0, "The actual run begins with climb.");
        Assert(ref checks, sample.Buttons == (ushort)(0xFFFF & ~RightThumbButton) &&
            sample.LeftTrigger == 32 && sample.RightTrigger == 201 && !sample.ContinuePressed,
            "Keep physical buttons and triggers, consume R3 only.");
        physical.Throttle = 0.9;
        sample = sequence.Tick(13.5, physical, true);
        Assert(ref checks, sample.Throttle == 0.42 && sample.Yaw == 0, "Capture throttle at Confirm.");
        sample = sequence.Tick(14, physical, true);
        Assert(ref checks, sample.Yaw == 1 && sample.Roll == 0 && sample.Pitch == 0, "Pulse begins after baseline.");
        sample = sequence.Tick(15.499, physical, true);
        Assert(ref checks, sample.Yaw == 1 && sequence.Recording, "Pulse lasts the configured real seconds.");
        sample = sequence.Tick(15.5, physical, true);
        Assert(ref checks, sample.Yaw == 0 && sequence.Recording, "Post-pulse baseline remains recorded.");
        sample = sequence.Tick(16, physical, true);
        Assert(ref checks, sequence.State == "Waiting" && !sequence.Recording && sequence.StepIndex == 2 &&
            sample.Yaw == physical.Yaw && firstClip != sequence.ClipName, "Stop recording and wait between steps.");
        sequence.Tick(100, physical, true);
        Assert(ref checks, sequence.State == "Waiting" && sequence.StepIndex == 2, "Never advance without Confirm.");
        sequence.Confirm(100, physical);
        sequence.Tick(103, physical, true);
        sample = sequence.Tick(103.1, physical, false);
        Assert(ref checks, sequence.State == "Aborted" && !sequence.Recording && !sequence.Complete &&
            sample.Throttle == physical.Throttle && sequence.StepIndex == 2, "Focus loss restores physical input and aborts.");
        sequence.Confirm(104, physical);
        sequence.Tick(105, physical, true);
        Assert(ref checks, sequence.State == "Aborted", "An aborted run cannot restart through Confirm.");

        sequence.Start(200);
        sequence.Confirm(200, physical);
        sequence.Tick(203, physical, false);
        Assert(ref checks, sequence.State == "Aborted" && !sequence.Recording, "Focus is required at countdown end.");
        sequence.Start(300);
        sequence.Confirm(300, physical);
        sample = sequence.Tick(310, physical, true);
        Assert(ref checks, sample.Throttle == 1 && sequence.Recording, "Late countdown tick starts a full actual climb.");
        sequence.Abort("erro injetado");
        sample = sequence.Tick(311, physical, true);
        Assert(ref checks, sequence.State == "Aborted" && !sequence.Recording &&
            sequence.Status.IndexOf("erro injetado", StringComparison.Ordinal) >= 0 && sample.Yaw == physical.Yaw,
            "Injected interruption is terminal and transparent to physical input.");

        CalibratorSequence shortBaseline = new CalibratorSequence(new CalibratorSettings {
            VerticalTest = false, AllRates = false, ClimbSeconds = 0,
            BaselineSeconds = 0.25, PulseSeconds = 0.25 });
        shortBaseline.Start(0);
        shortBaseline.Confirm(0, physical);
        shortBaseline.Tick(3, physical, true);
        shortBaseline.Tick(3.75, physical, true);
        Assert(ref checks, shortBaseline.Recording, "Recovery lasts 0.5 seconds regardless of the initial baseline setting.");
        shortBaseline.Tick(4, physical, true);
        Assert(ref checks, shortBaseline.State == "Waiting" && !shortBaseline.Recording,
            "Total duration includes the configured baseline, pulse and fixed recovery.");

        CalibratorSequence vertical = new CalibratorSequence(new CalibratorSettings {
            AllRates = false, ClimbSeconds = 0.5, VerticalTimeout = 2 });
        vertical.Start(0);
        vertical.Confirm(0, physical);
        vertical.Tick(3, physical, true);
        vertical.Confirm(3.1, physical);
        Assert(ref checks, vertical.State == "RunningVertical", "Cannot confirm during climb.");
        sample = vertical.Tick(3.5, physical, true);
        Assert(ref checks, sample.Throttle == 0 && vertical.Recording, "Vertical cuts throttle and continues recording.");
        vertical.Tick(4, physical, true);
        Assert(ref checks, vertical.State == "RunningVertical" && vertical.StepIndex == 1,
            "No automatic impact detection or R3 interpretation.");
        vertical.Confirm(4, physical);
        Assert(ref checks, vertical.State == "Waiting" && vertical.StepIndex == 2 && !vertical.Recording,
            "Observed impact confirmation ends the vertical clip.");
        vertical.Start(10);
        vertical.Confirm(10, physical);
        vertical.Tick(13, physical, true);
        sample = vertical.Tick(15, physical, true);
        Assert(ref checks, vertical.State == "Aborted" && !vertical.Recording && sample.Throttle == physical.Throttle,
            "Vertical timeout aborts without advancing.");

        CalibratorSequence rates = new CalibratorSequence(new CalibratorSettings { VerticalTest = false,
            Repetitions = 3, ClimbSeconds = 0, BaselineSeconds = 0.5, PulseSeconds = 0.5 });
        Assert(ref checks, rates.StepCount == 36, "All axes, percentages and repetitions are scheduled.");
        rates.Start(0);
        double now = 0;
        HashSet<string> names = new HashSet<string>();
        for (int i = 0; i < rates.StepCount; i++)
        {
            Assert(ref checks, names.Add(rates.ClipName) && AsciiClip(rates.ClipName), "Each clip has a unique safe ASCII name.");
            physical.Throttle = i % 2 == 0 ? 8 : -8;
            rates.Confirm(now, physical);
            now += 3;
            sample = rates.Tick(now, physical, true);
            Assert(ref checks, sample.Throttle == (i % 2 == 0 ? 1 : 0), "Throttle clamps to normalized range.");
            sample = rates.Tick(now + 0.5, physical, true);
            double expected = ((i / 3) % 4 + 1) / 4.0;
            double actual = i < 12 ? sample.Yaw : i < 24 ? sample.Roll : sample.Pitch;
            Assert(ref checks, actual == expected && sample.Yaw >= -1 && sample.Yaw <= 1 &&
                sample.Roll >= -1 && sample.Roll <= 1 && sample.Pitch >= -1 && sample.Pitch <= 1,
                "Requested positive axis percentage remains normalized.");
            now += 1.5;
            rates.Tick(now, physical, true);
        }
        Assert(ref checks, rates.Complete && rates.State == "Complete" && !rates.Recording &&
            rates.StepIndex == rates.StepCount, "The final step completes and stops recording.");
        rates.Start(now);
        Assert(ref checks, !names.Contains(rates.ClipName), "Restart creates a new clip name.");
        physical.Throttle = Double.NaN;
        rates.Confirm(now, physical);
        sample = rates.Tick(now + 3, physical, true);
        Assert(ref checks, sample.Throttle == 0, "Nonfinite throttle is sanitized.");
        rates.Tick(Double.NaN, physical, true);
        Assert(ref checks, rates.State == "Aborted" && !rates.Recording, "Nonfinite clock aborts.");
        rates.Start(0);
        rates.Tick(-1, physical, true);
        Assert(ref checks, rates.State == "Aborted", "Regressive clock aborts.");
        rates.Start(0);
        rates.Tick(0, null, true);
        Assert(ref checks, rates.State == "Aborted", "Missing physical input aborts.");

        CalibratorSettings[] invalid = new CalibratorSettings[] {
            new CalibratorSettings { ClimbSeconds = Double.NaN }, new CalibratorSettings { ClimbSeconds = 1.6 },
            new CalibratorSettings { PulseSeconds = 0.2 }, new CalibratorSettings { PulseSeconds = 4.1 },
            new CalibratorSettings { BaselineSeconds = 0.09 }, new CalibratorSettings { BaselineSeconds = 2.1 },
            new CalibratorSettings { VerticalTimeout = 1.9 }, new CalibratorSettings { VerticalTimeout = Double.PositiveInfinity },
            new CalibratorSettings { Repetitions = 0 }, new CalibratorSettings { Repetitions = 4 }
        };
        foreach (CalibratorSettings item in invalid)
        {
            bool rejected = false;
            try { new CalibratorSequence(item); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(ref checks, rejected, "Invalid settings must be rejected.");
        }
        ControlledStartTests(ref checks);
        FocusDisabledTests(ref checks);
        ThrottleTests(ref checks);
        return checks;
    }

    private static void ThrottleTests(ref int checks)
    {
        var physical = new ControlSample { Throttle = 1, Yaw = -1, Roll = 1, Pitch = -1,
            Buttons = 0xFFFF, LeftTrigger = 255, RightTrigger = 255, ContinuePressed = true };
        var source = new CalibratorSettings { ThrottleTest = true, ControlledStart = true,
            RequireFocus = false, Repetitions = 2, ThrottlePulseSeconds = 1 };
        var sequence = new CalibratorSequence(source);
        source.ThrottlePulseSeconds = 2;
        source.ThrottleTest = false;
        sequence.Start(0);
        Assert(ref checks, sequence.StepCount == 6 && IsNeutral(sequence.Tick(0, physical, false)), "Throttle mode schedules only three powers with requested repetitions and starts neutral.");
        var names = new HashSet<string>();
        for (int i = 0; i < 6; i++)
        {
            double now = i * 10;
            double expected = (i / 2 + 1) * 0.25;
            Assert(ref checks, names.Add(sequence.ClipName) && sequence.ClipName.StartsWith("throttle_") && AsciiClip(sequence.ClipName), "Throttle clips have unique identifiable names.");
            sequence.Confirm(now, physical);
            Assert(ref checks, IsNeutral(sequence.Tick(now + 0.999, physical, false)), "Throttle mode begins with one second of zero, not a launch boost.");
            var sample = sequence.Tick(now + 1, physical, false);
            Assert(ref checks, sequence.State == "RunningThrottle" && sequence.Recording && sample.Throttle == expected && NeutralAxesAndButtons(sample), "Throttle pulse uses requested power only, without physical axes, buttons, triggers or ARM.");
            sequence.Confirm(now + 1.5, physical);
            sample = sequence.Tick(now + 1.999, physical, false);
            Assert(ref checks, sequence.Recording && sample.Throttle == expected && NeutralAxesAndButtons(sample), "Early F10 cannot shorten the pulse; settings were copied.");
            sample = sequence.Tick(now + 2, physical, false);
            Assert(ref checks, IsNeutral(sample) && sequence.Recording, "At pulse end power is cut but video continues.");
            sample = sequence.Tick(now + 3, physical, false);
            Assert(ref checks, IsNeutral(sample) && sequence.StepIndex == i + 1 && sequence.Recording, "Fall waits for manual observation, not automatic impact detection.");
            sequence.Confirm(now + 3, physical);
            Assert(ref checks, IsNeutral(sequence.Tick(now + 3, physical, false)) && !sequence.Recording, "Confirming after pulse ends clip at zero.");
        }
        Assert(ref checks, sequence.Complete && sequence.StepCount == 6, "Throttle series ends without rotation or 100 percent tests.");
        sequence.Start(100);
        sequence.Confirm(100, physical);
        sequence.Tick(101, physical, false);
        Assert(ref checks, IsNeutral(sequence.Tick(121, physical, false)) && sequence.State == "Aborted" && !sequence.Recording, "Fall timeout stops at zero.");
        sequence.Start(130);
        sequence.Confirm(130, physical);
        sequence.Tick(131, physical, false);
        sequence.Abort("F11");
        Assert(ref checks, IsNeutral(sequence.Tick(131.1, physical, false)) && !sequence.Recording, "Emergency stop removes throttle immediately.");
        sequence.Start(140);
        sequence.Confirm(140, physical);
        Assert(ref checks, IsNeutral(sequence.Tick(141, null, false)) && sequence.State == "Aborted", "Physical disconnect stops the throttle test.");
        foreach (double value in new[] { Double.NaN, Double.PositiveInfinity, 0.24, 2.01 })
        {
            bool rejected = false;
            try { new CalibratorSequence(new CalibratorSettings { ThrottlePulseSeconds = value }); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(ref checks, rejected, "Throttle duration is finite and bounded.");
        }
        bool invalidMode = false;
        try { new CalibratorSequence(new CalibratorSettings { ThrottleTest = true }); }
        catch (ArgumentException) { invalidMode = true; }
        Assert(ref checks, invalidMode, "Throttle test cannot use uncontrolled physical passthrough.");
        invalidMode = false;
        try { new CalibratorSequence(new CalibratorSettings { ThrottleTest = true, ControlledStart = true, ThrottlePulseSeconds = 2, VerticalTimeout = 2 }); }
        catch (ArgumentException) { invalidMode = true; }
        Assert(ref checks, invalidMode, "Throttle test requires a remaining fall window.");
        sequence.Start(150);
        sequence.Confirm(150, physical);
        var delayed = sequence.Tick(155, physical, false);
        Assert(ref checks, delayed.Throttle == 0.25 && NeutralAxesAndButtons(delayed) && sequence.Recording,
            "A late preparation tick still starts the requested pulse, never a 100 percent boost.");
        Assert(ref checks, sequence.Tick(155.999, physical, false).Throttle == 0.25 &&
            IsNeutral(sequence.Tick(156, physical, false)), "The complete pulse lasts from its actual first output tick.");
    }

    private static void ControlledStartTests(ref int checks)
    {
        ControlSample physical = new ControlSample { Throttle = 0.5, Yaw = 0.8, Roll = -0.9,
            Pitch = 1, Buttons = 0xFFFF, LeftTrigger = 255, RightTrigger = 255, ContinuePressed = true };
        CalibratorSettings source = new CalibratorSettings { ControlledStart = true, VerticalTest = false,
            AllRates = false, ClimbSeconds = 0.25, BaselineSeconds = 0.25, PulseSeconds = 0.25 };
        CalibratorSequence controlled = new CalibratorSequence(source);
        source.ControlledStart = false;
        source.RotationThrottle = 0.8;
        Assert(ref checks, IsNeutral(controlled.Tick(0, physical, true)), "Controlled idle ignores physical stick spring and buttons.");
        controlled.Start(0);
        Assert(ref checks, IsNeutral(controlled.Tick(0, physical, false)) && controlled.State == "Waiting",
            "Controlled waiting stays neutral without requiring focus yet.");
        Assert(ref checks, controlled.Status.IndexOf("ARM manual", StringComparison.Ordinal) >= 0 &&
            controlled.Status.IndexOf("não detecta pouso", StringComparison.Ordinal) >= 0,
            "Waiting explains manual ARM and that zero throttle cannot detect landing.");
        controlled.Confirm(0, physical);
        Assert(ref checks, controlled.State == "Preparing" && !controlled.Recording, "Confirm starts preparation without a countdown.");
        ControlSample sample = controlled.Tick(0, physical, true);
        Assert(ref checks, IsNeutral(sample), "Preparation starts with zero throttle, neutral axes, no buttons or triggers.");
        Assert(ref checks, IsNeutral(controlled.Tick(0.119, physical, true)) &&
            IsNeutral(controlled.Tick(0.12, physical, true)), "Preparation never injects an L1 arm pulse.");
        sample = controlled.Tick(0.999, physical, true);
        Assert(ref checks, IsNeutral(sample) && controlled.State == "Preparing" && !controlled.Recording,
            "Preparation lasts a full second without recording.");
        sample = controlled.Tick(1, physical, true);
        Assert(ref checks, controlled.State == "RunningRotation" && controlled.Recording && sample.Throttle == 1,
            "Recording and full-power climb start exactly at one second.");
        Assert(ref checks, NeutralAxesAndButtons(sample), "Physical axes, buttons and triggers cannot tilt or arm during controlled climb.");
        sample = controlled.Tick(1.25, physical, true);
        Assert(ref checks, sample.Throttle == 0.35 && NeutralAxesAndButtons(sample),
            "Rotation uses fixed 35 percent instead of the physical 50 percent or mutated source settings.");
        physical.Throttle = 0.9;
        sample = controlled.Tick(1.5, physical, true);
        Assert(ref checks, sample.Throttle == 0.35 && sample.Yaw == 1 && sample.Roll == 0 && sample.Pitch == 0 &&
            sample.Buttons == 0 && sample.LeftTrigger == 0 && sample.RightTrigger == 0,
            "Only the scheduled axis pulses; physical 90 percent cannot alter rotation throttle.");
        sample = controlled.Tick(1.75, physical, true);
        Assert(ref checks, sample.Throttle == 0.35 && NeutralAxesAndButtons(sample) && controlled.Recording,
            "Recovery keeps configured throttle and neutral controls.");
        sample = controlled.Tick(2.25, physical, true);
        Assert(ref checks, IsNeutral(sample) && controlled.State == "Waiting" && !controlled.Recording,
            "Ending a controlled step immediately returns zero instead of physical input.");
        for (int i = 1; i < 3; i++)
        {
            double now = i * 3;
            controlled.Confirm(now, physical);
            Assert(ref checks, IsNeutral(controlled.Tick(now, physical, true)), "Each next step repeats neutral preparation.");
            controlled.Tick(now + 1, physical, true);
            sample = controlled.Tick(now + 2.25, physical, true);
            Assert(ref checks, IsNeutral(sample) && !controlled.Recording, "Each clip ends at neutral throttle.");
        }
        Assert(ref checks, controlled.Complete && controlled.State == "Complete" &&
            IsNeutral(controlled.Tick(100, physical, true)), "Completed controlled sessions remain neutral.");
        controlled.Start(101);
        controlled.Confirm(101, physical);
        controlled.Tick(102, physical, true);
        controlled.Abort("erro injetado");
        Assert(ref checks, IsNeutral(controlled.Tick(102.1, physical, true)) && !controlled.Recording,
            "Explicit controlled abort immediately removes all output.");
        controlled.Start(200);
        controlled.Confirm(200, physical);
        sample = controlled.Tick(200, physical, false);
        Assert(ref checks, IsNeutral(sample) && controlled.State == "Aborted" && !controlled.Recording &&
            controlled.StepIndex == 1, "Preparation focus loss aborts at zero without advancing.");
        controlled.Start(300);
        controlled.Confirm(300, physical);
        controlled.Tick(301, physical, true);
        sample = controlled.Tick(301.1, physical, false);
        Assert(ref checks, IsNeutral(sample) && controlled.State == "Aborted" && !controlled.Recording &&
            controlled.StepIndex == 1, "Running focus loss aborts at zero without advancing.");
        controlled.Start(400);
        controlled.Confirm(400, physical);
        sample = controlled.Tick(405, physical, true);
        Assert(ref checks, sample.Throttle == 1 && controlled.Recording,
            "A late preparation tick still begins a complete actual climb.");

        CalibratorSequence vertical = new CalibratorSequence(new CalibratorSettings { ControlledStart = true, AllRates = false });
        vertical.Start(0);
        vertical.Confirm(0, physical);
        Assert(ref checks, IsNeutral(vertical.Tick(0, physical, true)), "Vertical preparation also injects no ARM or tilt.");
        sample = vertical.Tick(1, physical, true);
        Assert(ref checks, sample.Throttle == 1 && NeutralAxesAndButtons(sample) && vertical.State == "RunningVertical",
            "Controlled vertical starts its full-power climb after preparation.");
        sample = vertical.Tick(1.5, physical, true);
        Assert(ref checks, IsNeutral(sample) && vertical.Recording, "Controlled vertical cuts power and stays neutral until manual impact confirmation.");
        vertical.Confirm(1.5, physical);
        Assert(ref checks, IsNeutral(vertical.Tick(1.5, physical, true)) && vertical.State == "Waiting",
            "Controlled vertical confirmation ends recording and keeps zero output.");
        foreach (double value in new double[] { Double.NaN, Double.PositiveInfinity, -0.01, 1.01 })
        {
            bool rejected = false;
            try { new CalibratorSequence(new CalibratorSettings { RotationThrottle = value }); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(ref checks, rejected, "RotationThrottle must be finite and normalized.");
        }
    }

    private static void FocusDisabledTests(ref int checks)
    {
        ControlSample physical = new ControlSample { Throttle = 0.5, Yaw = 1, Buttons = 0xFFFF };
        var source = new CalibratorSettings { ControlledStart = true, RequireFocus = false };
        var sequence = new CalibratorSequence(source);
        source.RequireFocus = true;
        sequence.Start(0);
        Assert(ref checks, sequence.StepCount == 13, "Independent mode keeps the full 13-step series.");
        double now = 0;
        for (int step = 1; step <= 13; step++)
        {
            Assert(ref checks, sequence.State == "Waiting" && sequence.StepIndex == step, "Each step waits for F10.");
            sequence.Confirm(now, physical);
            Assert(ref checks, IsNeutral(sequence.Tick(now + 0.999, physical, false)) && sequence.State == "Preparing", "Focus-free mode holds zero for a full second.");
            var sample = sequence.Tick(now + 1, physical, false);
            Assert(ref checks, sample.Throttle == 1 && NeutralAxesAndButtons(sample) && sequence.Recording, "Focus-free mode climbs after one second without ARM.");
            if (step == 1)
            {
                sample = sequence.Tick(now + 1.5, physical, false);
                Assert(ref checks, IsNeutral(sample) && sequence.State == "RunningVertical", "Vertical cuts power without a focus gate.");
                sequence.Confirm(now + 2, physical);
            }
            else
            {
                sample = sequence.Tick(now + 2, physical, false);
                int axis = (step - 2) / 4, percent = ((step - 2) % 4 + 1) * 25;
                double expected = percent / 100.0;
                Assert(ref checks, sample.Throttle == 0.35 && sample.Yaw == (axis == 0 ? expected : 0) && sample.Roll == (axis == 1 ? expected : 0) && sample.Pitch == (axis == 2 ? expected : 0), "Focus-free mode sends the scheduled axis and power.");
                sequence.Tick(now + 4, physical, false);
            }
            Assert(ref checks, IsNeutral(sequence.Tick(now + 4, physical, false)) && !sequence.Recording, "Each completed step releases output without focus.");
            now += 5;
        }
        Assert(ref checks, sequence.Complete && sequence.StepIndex == 13, "Independent mode ends after one game.");
        sequence.Start(100);
        sequence.Confirm(100, physical);
        sequence.Tick(101, physical, false);
        sequence.Abort("F11");
        Assert(ref checks, IsNeutral(sequence.Tick(101.1, physical, false)) && sequence.State == "Aborted", "F11 still releases output without focus.");
        sequence.Start(200);
        sequence.Confirm(200, physical);
        Assert(ref checks, IsNeutral(sequence.Tick(201, null, false)) && sequence.State == "Aborted", "Physical disconnect remains a stop condition.");
    }
    private static bool NeutralAxesAndButtons(ControlSample sample)
    {
        return sample.Yaw == 0 && sample.Roll == 0 && sample.Pitch == 0 && sample.Buttons == 0 &&
            sample.LeftTrigger == 0 && sample.RightTrigger == 0 && !sample.ContinuePressed;
    }

    private static bool IsNeutral(ControlSample sample)
    {
        return sample.Throttle == 0 && NeutralAxesAndButtons(sample);
    }

    private static bool AsciiClip(string value)
    {
        foreach (char c in value)
            if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
        return value.Length > 0;
    }

    private static void Assert(ref int checks, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("CalibratorSequence.SelfTest: " + message);
        checks++;
    }
}
