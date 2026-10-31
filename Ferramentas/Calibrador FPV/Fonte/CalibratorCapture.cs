using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

// Classic AVI is limited to 2 GB; start another recording when this limit is reached.
public sealed class CalibratorCapture : IDisposable
{
    private const int Rate = 30;
    private readonly int width, height;
    private readonly List<uint> offsets = new List<uint>();
    private readonly List<uint> lengths = new List<uint>();
    private BinaryWriter writer;
    private StreamWriter csv;
    private long totalFramesPosition, streamLengthPosition, bufferPosition, streamBufferPosition;
    private long moviSizePosition, moviTypePosition;
    private uint largestFrame;
    private byte[] lastJpeg;
    private double lastSourceSeconds, lastInputSeconds = -1;
    private string lastAnnotation;
    private bool completed, failed;

    public long Frames { get; private set; }
    public long DuplicatedFrames { get; private set; }

    public CalibratorCapture(string directory, int width, int height, int fps)
    {
        if (fps != Rate || width <= 0 || height <= 0 || width > 32767 || height > 32767 ||
            (long)width * height * 3 > Int32.MaxValue)
            throw new IOException("MJPEG AVI requires 30 fps and supported positive dimensions.");
        this.width = width;
        this.height = height;
        Directory.CreateDirectory(directory);
        string aviPath = Path.Combine(directory, "clip.avi");
        string csvPath = Path.Combine(directory, "frames.csv");
        bool aviCreated = false, csvCreated = false;
        try
        {
            writer = new BinaryWriter(new FileStream(aviPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read));
            aviCreated = true;
            FileStream csvStream = new FileStream(csvPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            csvCreated = true;
            csv = new StreamWriter(csvStream, new UTF8Encoding(false));
            csv.WriteLine("frame,video_seconds,source_elapsed_seconds,observed_elapsed_seconds,is_duplicate,annotation");
            WriteHeaders();
        }
        catch
        {
            if (writer != null) writer.Dispose();
            if (csv != null) csv.Dispose();
            if (aviCreated) File.Delete(aviPath);
            if (csvCreated) File.Delete(csvPath);
            throw;
        }
    }

    private void FourCC(string value) { writer.Write(Encoding.ASCII.GetBytes(value)); }
    private void U(uint value) { writer.Write(value); }
    private void Patch(long position, uint value)
    {
        long end = writer.BaseStream.Position;
        writer.BaseStream.Position = position;
        U(value);
        writer.BaseStream.Position = end;
    }

    private void WriteHeaders()
    {
        FourCC("RIFF"); U(0); FourCC("AVI ");
        FourCC("LIST"); long hdrlSize = writer.BaseStream.Position; U(0); FourCC("hdrl");
        FourCC("avih"); U(56); U(1000000 / Rate); U(0); U(0); U(0x10);
        totalFramesPosition = writer.BaseStream.Position; U(0);
        U(0); U(1); bufferPosition = writer.BaseStream.Position; U(0);
        U((uint)width); U((uint)height); U(0); U(0); U(0); U(0);
        FourCC("LIST"); long strlSize = writer.BaseStream.Position; U(0); FourCC("strl");
        FourCC("strh"); U(56); FourCC("vids"); FourCC("MJPG"); U(0);
        writer.Write((ushort)0); writer.Write((ushort)0); U(0); U(1); U(Rate); U(0);
        streamLengthPosition = writer.BaseStream.Position; U(0);
        streamBufferPosition = writer.BaseStream.Position; U(0); U(UInt32.MaxValue); U(0);
        writer.Write((short)0); writer.Write((short)0); writer.Write((short)width); writer.Write((short)height);
        FourCC("strf"); U(40); U(40); writer.Write(width); writer.Write(height);
        writer.Write((ushort)1); writer.Write((ushort)24); FourCC("MJPG");
        U((uint)(width * height * 3)); U(0); U(0); U(0); U(0);
        Patch(strlSize, (uint)(writer.BaseStream.Position - strlSize - 4));
        Patch(hdrlSize, (uint)(writer.BaseStream.Position - hdrlSize - 4));
        FourCC("LIST"); moviSizePosition = writer.BaseStream.Position; U(0);
        moviTypePosition = writer.BaseStream.Position; FourCC("movi");
    }

    // elapsedSeconds is stopwatch time in seconds, never milliseconds.
    public void AddFrame(Bitmap image, double elapsedSeconds, string annotation)
    {
        if (completed || failed) throw new IOException("The recording is closed or has failed.");
        if (image == null || Double.IsNaN(elapsedSeconds) || Double.IsInfinity(elapsedSeconds) ||
            elapsedSeconds < 0 || elapsedSeconds < lastInputSeconds || elapsedSeconds * Rate > Int32.MaxValue)
            throw new IOException("A valid image and nondecreasing elapsed time in seconds are required.");
        long slot = (long)Math.Floor(elapsedSeconds * Rate + 0.0000001);
        lastInputSeconds = elapsedSeconds;
        if (slot < Frames) return;
        byte[] jpeg;
        try { jpeg = Encode(image, annotation); }
        catch (IOException) { throw; }
        catch (Exception ex) { throw new IOException("The frame could not be encoded as MJPEG.", ex); }
        // Preflight the whole gap so a bad timestamp cannot partially fill the file.
        long oldCount = Math.Max(0, slot - Frames);
        long oldLength = lastJpeg == null ? jpeg.Length : lastJpeg.Length;
        long projected = writer.BaseStream.Position + oldCount * (8 + oldLength + (oldLength & 1)) +
            8 + jpeg.Length + (jpeg.Length & 1) + 8 + (slot + 1) * 16;
        if (projected > Int32.MaxValue) throw new IOException("The 2 GB AVI capacity would be exceeded.");
        try
        {
            while (Frames < slot)
                WriteFrame(lastJpeg ?? jpeg, lastJpeg == null ? elapsedSeconds : lastSourceSeconds,
                    elapsedSeconds, true, lastJpeg == null ? annotation : lastAnnotation);
            WriteFrame(jpeg, elapsedSeconds, elapsedSeconds, false, annotation);
            lastJpeg = jpeg;
            lastSourceSeconds = elapsedSeconds;
            lastAnnotation = annotation;
        }
        catch (IOException) { failed = true; throw; }
        catch (Exception ex)
        {
            failed = true;
            throw new IOException("The frame could not be encoded or recorded as MJPEG.", ex);
        }
    }

    private byte[] Encode(Bitmap image, string annotation)
    {
        using (Bitmap copy = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        using (Graphics graphics = Graphics.FromImage(copy))
        {
            graphics.DrawImage(image, new Rectangle(0, 0, width, height));
            if (!String.IsNullOrEmpty(annotation))
            {
                using (Font font = new Font(FontFamily.GenericSansSerif, 12))
                {
                    SizeF size = graphics.MeasureString(annotation, font, Math.Max(1, width - 12));
                    graphics.FillRectangle(Brushes.Black, 2, 2, Math.Min(width, size.Width + 8), Math.Min(height, size.Height + 8));
                    graphics.DrawString(annotation, font, Brushes.White, new RectangleF(6, 6, Math.Max(1, width - 12), Math.Max(1, height - 12)));
                }
            }
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo candidate in ImageCodecInfo.GetImageEncoders())
                if (candidate.FormatID == ImageFormat.Jpeg.Guid) { codec = candidate; break; }
            if (codec == null) throw new IOException("A JPEG encoder is unavailable.");
            using (MemoryStream data = new MemoryStream())
            using (EncoderParameters parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                copy.Save(data, codec, parameters);
                return data.ToArray();
            }
        }
    }

    private static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    private void WriteFrame(byte[] jpeg, double sourceSeconds, double observedSeconds, bool duplicate, string annotation)
    {
        offsets.Add((uint)(writer.BaseStream.Position - moviTypePosition));
        lengths.Add((uint)jpeg.Length);
        FourCC("00dc"); U((uint)jpeg.Length); writer.Write(jpeg);
        if ((jpeg.Length & 1) != 0) writer.Write((byte)0);
        csv.WriteLine(Frames.ToString(CultureInfo.InvariantCulture) + "," + Number(Frames / (double)Rate) + "," +
            Number(sourceSeconds) + "," + Number(observedSeconds) + "," + (duplicate ? "1" : "0") +
            ",\"" + (annotation ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"");
        largestFrame = Math.Max(largestFrame, (uint)jpeg.Length);
        Frames++;
        if (duplicate) DuplicatedFrames++;
    }

    public void Complete()
    {
        if (completed) return;
        completed = true;
        try
        {
            if (failed) return;
            Patch(moviSizePosition, (uint)(writer.BaseStream.Position - moviTypePosition));
            FourCC("idx1"); U((uint)(Frames * 16));
            for (int i = 0; i < offsets.Count; i++)
            {
                FourCC("00dc"); U(0x10); U(offsets[i]); U(lengths[i]);
            }
            Patch(totalFramesPosition, (uint)Frames);
            Patch(streamLengthPosition, (uint)Frames);
            Patch(bufferPosition, largestFrame);
            Patch(streamBufferPosition, largestFrame);
            Patch(4, (uint)(writer.BaseStream.Length - 8));
            writer.Flush();
            csv.Flush();
        }
        finally
        {
            try { if (csv != null) csv.Dispose(); }
            finally { if (writer != null) writer.Dispose(); }
        }
    }

    public void Dispose() { Complete(); }

    private struct Chunk { public long Data; public uint Size; }
    private static string ReadFourCC(BinaryReader reader) { return Encoding.ASCII.GetString(reader.ReadBytes(4)); }
    private static Chunk FindChunk(BinaryReader reader, long start, long end, string id)
    {
        for (long position = start; position + 8 <= end; )
        {
            reader.BaseStream.Position = position;
            string name = ReadFourCC(reader);
            uint size = reader.ReadUInt32();
            long next = position + 8 + size + (size & 1);
            if (next > end) throw new IOException("Invalid AVI chunk boundary.");
            if (name == id) return new Chunk { Data = position + 8, Size = size };
            if (name == "LIST" && size >= 4)
            {
                string type = ReadFourCC(reader);
                if (type == id) return new Chunk { Data = position + 8, Size = size };
                if (type != "movi")
                {
                    Chunk found = FindChunk(reader, position + 12, position + 8 + size, id);
                    if (found.Data != 0) return found;
                }
            }
            position = next;
        }
        return new Chunk();
    }

    private static void Check(bool condition, ref int assertions)
    {
        if (!condition) throw new IOException("MJPEG AVI self-test assertion " + (assertions + 1) + " failed.");
        assertions++;
    }

    // Use an empty test directory: like production, the self-test never overwrites files.
    public static int SelfTest(string directory)
    {
        int assertions = 0;
        using (Bitmap image = new Bitmap(80, 60))
        using (CalibratorCapture capture = new CalibratorCapture(directory, 64, 48, Rate))
        {
            using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);
            capture.AddFrame(image, 0, "first, \"red\"");
            using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Green);
            capture.AddFrame(image, 1.0 / Rate, "green");
            using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Blue);
            capture.AddFrame(image, 0.2, "blue");
            capture.Complete();
            capture.Complete();
            Check(capture.Frames == 7 && capture.DuplicatedFrames == 4, ref assertions);
            Check(image.Width == 80, ref assertions);
        }
        using (BinaryReader reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "clip.avi"))))
        {
            Check(ReadFourCC(reader) == "RIFF", ref assertions);
            Check(reader.ReadUInt32() == reader.BaseStream.Length - 8, ref assertions);
            Check(ReadFourCC(reader) == "AVI ", ref assertions);
            Chunk avih = FindChunk(reader, 12, reader.BaseStream.Length, "avih");
            Chunk strh = FindChunk(reader, 12, reader.BaseStream.Length, "strh");
            Chunk strf = FindChunk(reader, 12, reader.BaseStream.Length, "strf");
            Chunk movi = FindChunk(reader, 12, reader.BaseStream.Length, "movi");
            Chunk index = FindChunk(reader, 12, reader.BaseStream.Length, "idx1");
            Check(avih.Size == 56 && strh.Size == 56 && strf.Size == 40 && movi.Data > 0 && index.Size == 112, ref assertions);
            reader.BaseStream.Position = avih.Data;
            Check(reader.ReadUInt32() == 33333, ref assertions);
            reader.BaseStream.Position = avih.Data + 16;
            Check(reader.ReadUInt32() == 7, ref assertions);
            reader.BaseStream.Position = strh.Data;
            Check(ReadFourCC(reader) == "vids" && ReadFourCC(reader) == "MJPG", ref assertions);
            reader.BaseStream.Position = strh.Data + 20;
            Check(reader.ReadUInt32() == 1 && reader.ReadUInt32() == 30, ref assertions);
            reader.BaseStream.Position = strh.Data + 32;
            Check(reader.ReadUInt32() == 7, ref assertions);
            reader.BaseStream.Position = strf.Data + 4;
            Check(reader.ReadInt32() == 64 && reader.ReadInt32() == 48, ref assertions);
            long expected = movi.Data + 4;
            byte[] previous = null;
            for (int i = 0; i < 7; i++)
            {
                reader.BaseStream.Position = index.Data + i * 16;
                Check(ReadFourCC(reader) == "00dc" && reader.ReadUInt32() == 0x10, ref assertions);
                uint offset = reader.ReadUInt32(), size = reader.ReadUInt32();
                Check(movi.Data + offset == expected, ref assertions);
                reader.BaseStream.Position = expected;
                Check(ReadFourCC(reader) == "00dc" && reader.ReadUInt32() == size, ref assertions);
                byte[] jpeg = reader.ReadBytes((int)size);
                using (MemoryStream data = new MemoryStream(jpeg))
                using (Image decoded = Image.FromStream(data))
                    Check(decoded.Width == 64 && decoded.Height == 48, ref assertions);
                if (i >= 2 && i <= 5)
                    Check(Convert.ToBase64String(jpeg) == Convert.ToBase64String(previous), ref assertions);
                previous = jpeg;
                expected += 8 + size + (size & 1);
            }
            Check(expected == movi.Data + movi.Size && expected == index.Data - 8, ref assertions);
        }
        string[] rows = File.ReadAllLines(Path.Combine(directory, "frames.csv"));
        Check(rows.Length == 8, ref assertions);
        Check(rows[1].Contains("\"first, \"\"red\"\"\"") && rows[3].Contains(",0.2,1,\"green\""), ref assertions);
        Check(rows[7].StartsWith("6,0.2,0.2,0.2,0,"), ref assertions);
        return assertions;
    }
}
