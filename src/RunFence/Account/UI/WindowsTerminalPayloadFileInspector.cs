namespace RunFence.Account.UI;

public class WindowsTerminalPayloadFileInspector
{
    public bool IsPortableExecutable(Stream stream, long length, bool rejectMalformedExecutableName)
    {
        if (length == 0)
            return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

        Span<byte> dosHeader = stackalloc byte[64];
        if (!TryReadExact(stream, dosHeader))
            return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

        if (dosHeader[0] != 'M' || dosHeader[1] != 'Z')
            return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

        var peHeaderOffset = BitConverter.ToInt32(dosHeader[60..64]);
        if (peHeaderOffset < dosHeader.Length || peHeaderOffset > length - 4)
            return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

        var remainingBytesToSignature = peHeaderOffset - dosHeader.Length;
        Span<byte> buffer = stackalloc byte[4096];
        while (remainingBytesToSignature > 0)
        {
            var readSize = Math.Min(buffer.Length, remainingBytesToSignature);
            if (!TryReadExact(stream, buffer[..readSize]))
                return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

            remainingBytesToSignature -= readSize;
        }

        Span<byte> peSignature = stackalloc byte[4];
        if (!TryReadExact(stream, peSignature))
            return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

        if (peSignature[0] != 'P' || peSignature[1] != 'E' || peSignature[2] != 0 || peSignature[3] != 0)
            return RejectMalformedExecutableEntryIfNeeded(rejectMalformedExecutableName);

        return true;
    }

    public bool HasExecutableExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase);
    }

    private bool RejectMalformedExecutableEntryIfNeeded(bool rejectMalformedExecutableName)
    {
        if (rejectMalformedExecutableName)
            throw new InvalidOperationException("Windows Terminal payload contains an invalid executable file.");

        return false;
    }

    private bool TryReadExact(Stream stream, Span<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            var read = stream.Read(buffer);
            if (read == 0)
                return false;

            buffer = buffer[read..];
        }

        return true;
    }
}
