using RunFence.Core;

namespace RunFence.Account.UI;

internal sealed class SecurePasswordEditBuffer : IDisposable
{
    private readonly ProtectedString _password = new();
    private char _pendingHighSurrogate;
    private bool _disposed;

    public int Length => _password.Length;
    public bool IsEmpty => _password.Length == 0;
    public ProtectedString Password => _password;

    public ProtectedString GetPassword()
    {
        var copy = _password.Copy();
        copy.MakeReadOnly();
        return copy;
    }

    public bool PasswordsMatch(SecurePasswordEditBuffer other) =>
        ProtectedString.ContentEqual(_password, other._password);

    public void SetFromProtectedString(ProtectedString? value)
    {
        _password.Clear();
        _pendingHighSurrogate = '\0';

        if (value is null || value.Length == 0)
            return;

        value.UseUtf16BytesSnapshot(_password.SetFromUtf16Bytes);
    }

    public void Clear()
    {
        _pendingHighSurrogate = '\0';
        _password.Clear();
    }

    public void ClearPendingHighSurrogate() => _pendingHighSurrogate = '\0';

    public SecurePasswordEditResult ApplyChar(char ch, int selectionStart, int selectionLength, int maxLength)
    {
        if (ch == '\b')
            return ApplyBackspace(selectionStart, selectionLength, maxLength);

        if (ch == (char)0x7F)
            return new SecurePasswordEditResult(false, selectionStart, selectionLength);

        if (char.IsControl(ch))
        {
            bool changed = FlushPendingHighSurrogate(ref selectionStart, ref selectionLength, maxLength);
            return new SecurePasswordEditResult(changed, selectionStart, selectionLength);
        }

        if (char.IsHighSurrogate(ch))
        {
            bool highSurrogateFlush = FlushPendingHighSurrogate(ref selectionStart, ref selectionLength, maxLength);
            _pendingHighSurrogate = ch;
            return new SecurePasswordEditResult(highSurrogateFlush, selectionStart, selectionLength);
        }

        if (char.IsLowSurrogate(ch) && _pendingHighSurrogate != '\0')
        {
            int currentLength = _password.Length - selectionLength;
            if (maxLength > 0 && currentLength + 2 > maxLength)
            {
                _pendingHighSurrogate = '\0';
                return new SecurePasswordEditResult(false, selectionStart, selectionLength);
            }

            RemoveSelection(selectionStart, selectionLength);
            _password.InsertAt(selectionStart, _pendingHighSurrogate);
            _password.InsertAt(selectionStart + 1, ch);
            _pendingHighSurrogate = '\0';
            return new SecurePasswordEditResult(true, selectionStart + 2, 0);
        }

        bool flushed = FlushPendingHighSurrogate(ref selectionStart, ref selectionLength, maxLength);

        int lengthAfterSelection = _password.Length - selectionLength;
        if (maxLength > 0 && lengthAfterSelection >= maxLength)
            return new SecurePasswordEditResult(flushed, selectionStart, selectionLength);

        RemoveSelection(selectionStart, selectionLength);
        _password.InsertAt(selectionStart, ch);
        _pendingHighSurrogate = '\0';
        return new SecurePasswordEditResult(true, selectionStart + 1, 0);
    }

    public SecurePasswordEditResult ApplyBackspace(int selectionStart, int selectionLength, int maxLength)
    {
        bool changed = FlushPendingHighSurrogate(ref selectionStart, ref selectionLength, maxLength);

        if (selectionLength > 0)
        {
            RemoveSelection(selectionStart, selectionLength);
            return new SecurePasswordEditResult(true, selectionStart, 0);
        }

        if (selectionStart <= 0)
            return new SecurePasswordEditResult(changed, selectionStart, 0);

        if (selectionStart >= 2
            && char.IsLowSurrogate(_password.CharAt(selectionStart - 1))
            && char.IsHighSurrogate(_password.CharAt(selectionStart - 2)))
        {
            _password.RemoveAt(selectionStart - 2);
            _password.RemoveAt(selectionStart - 2);
            return new SecurePasswordEditResult(true, selectionStart - 2, 0);
        }

        _password.RemoveAt(selectionStart - 1);
        return new SecurePasswordEditResult(true, selectionStart - 1, 0);
    }

    public SecurePasswordEditResult ApplyDelete(int selectionStart, int selectionLength, int maxLength)
    {
        bool changed = FlushPendingHighSurrogate(ref selectionStart, ref selectionLength, maxLength);

        if (selectionLength > 0)
        {
            RemoveSelection(selectionStart, selectionLength);
            return new SecurePasswordEditResult(true, selectionStart, 0);
        }

        if (selectionStart >= _password.Length)
            return new SecurePasswordEditResult(changed, selectionStart, 0);

        if (selectionStart + 1 < _password.Length
            && char.IsHighSurrogate(_password.CharAt(selectionStart))
            && char.IsLowSurrogate(_password.CharAt(selectionStart + 1)))
        {
            _password.RemoveAt(selectionStart);
            _password.RemoveAt(selectionStart);
            return new SecurePasswordEditResult(true, selectionStart, 0);
        }

        _password.RemoveAt(selectionStart);
        return new SecurePasswordEditResult(true, selectionStart, 0);
    }

    public SecurePasswordEditResult ApplyPaste(
        ISecurePasswordPasteSource source,
        int selectionStart,
        int selectionLength,
        int maxLength)
    {
        _pendingHighSurrogate = '\0';
        bool changed = selectionLength > 0;
        RemoveSelection(selectionStart, selectionLength);

        int inserted = 0;
        int available = maxLength == 0 ? int.MaxValue : maxLength - _password.Length;
        for (int i = 0; inserted < available; i++)
        {
            char c = source.ReadChar(i);
            if (c == '\0')
                break;

            char next = source.ReadChar(i + 1);
            if (char.IsHighSurrogate(c) && char.IsLowSurrogate(next))
            {
                if (available - inserted < 2)
                    break;

                _password.InsertAt(selectionStart + inserted, c);
                _password.InsertAt(selectionStart + inserted + 1, next);
                inserted += 2;
                i++;
                continue;
            }

            _password.InsertAt(selectionStart + inserted, c);
            inserted++;
        }

        return new SecurePasswordEditResult(changed || inserted > 0, selectionStart + inserted, 0);
    }

    public SecurePasswordEditResult ApplyClear(int selectionStart, int selectionLength)
    {
        _pendingHighSurrogate = '\0';
        if (selectionLength <= 0)
            return new SecurePasswordEditResult(false, selectionStart, 0);

        RemoveSelection(selectionStart, selectionLength);
        return new SecurePasswordEditResult(true, selectionStart, 0);
    }

    public SecurePasswordEditResult ApplyCopy(int selectionStart, int selectionLength) =>
        new(false, selectionStart, selectionLength);

    public SecurePasswordEditResult ApplyCut(int selectionStart, int selectionLength) =>
        new(false, selectionStart, selectionLength);

    public SecurePasswordEditResult ApplyUndo(int selectionStart, int selectionLength) =>
        new(false, selectionStart, selectionLength);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _password.Dispose();
    }

    private bool FlushPendingHighSurrogate(ref int selectionStart, ref int selectionLength, int maxLength)
    {
        if (_pendingHighSurrogate == '\0')
            return false;

        bool changed = selectionLength > 0;
        RemoveSelection(selectionStart, selectionLength);
        selectionLength = 0;

        if (maxLength == 0 || _password.Length < maxLength)
        {
            _password.InsertAt(selectionStart, _pendingHighSurrogate);
            selectionStart++;
            changed = true;
        }

        _pendingHighSurrogate = '\0';
        return changed;
    }

    private void RemoveSelection(int selectionStart, int selectionLength)
    {
        for (int i = 0; i < selectionLength; i++)
            _password.RemoveAt(selectionStart);
    }
}
