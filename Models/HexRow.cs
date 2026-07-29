using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace NexusProgrammer;

public sealed class HexRow : INotifyPropertyChanged
{
    private readonly byte[] _buffer;
    private readonly int _offset;
    private readonly Action<int, byte> _onChanged;

    public HexRow(byte[] buffer, int offset, Action<int, byte> onChanged)
    {
        _buffer = buffer;
        _offset = offset;
        _onChanged = onChanged;
    }

    public string Address => $"0x{_offset:X8}";
    public string B0 { get => Get(0); set => Set(0, value); }
    public string B1 { get => Get(1); set => Set(1, value); }
    public string B2 { get => Get(2); set => Set(2, value); }
    public string B3 { get => Get(3); set => Set(3, value); }
    public string B4 { get => Get(4); set => Set(4, value); }
    public string B5 { get => Get(5); set => Set(5, value); }
    public string B6 { get => Get(6); set => Set(6, value); }
    public string B7 { get => Get(7); set => Set(7, value); }
    public string B8 { get => Get(8); set => Set(8, value); }
    public string B9 { get => Get(9); set => Set(9, value); }
    public string BA { get => Get(10); set => Set(10, value); }
    public string BB { get => Get(11); set => Set(11, value); }
    public string BC { get => Get(12); set => Set(12, value); }
    public string BD { get => Get(13); set => Set(13, value); }
    public string BE { get => Get(14); set => Set(14, value); }
    public string BF { get => Get(15); set => Set(15, value); }

    public string Ascii
    {
        get
        {
            var builder = new StringBuilder(16);
            for (var i = 0; i < 16; i++)
            {
                var value = ReadByte(i);
                builder.Append(value is >= 32 and <= 126 ? (char)value : '.');
            }

            return builder.ToString();
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            for (var i = 0; i < 16 && i < value.Length && _offset + i < _buffer.Length; i++)
            {
                var next = value[i] is >= ' ' and <= '~' ? (byte)value[i] : (byte)'.';
                _onChanged(_offset + i, next);
                OnPropertyChanged(CellName(i));
            }

            RefreshAscii();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshAscii() => OnPropertyChanged(nameof(Ascii));

    private string Get(int index) => _offset + index < _buffer.Length ? _buffer[_offset + index].ToString("X2") : string.Empty;

    private void Set(int index, string value)
    {
        if (_offset + index >= _buffer.Length)
        {
            return;
        }

        value = value.Trim();
        if (value.Length > 2)
        {
            value = value[^2..];
        }

        if (byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            _onChanged(_offset + index, parsed);
            OnPropertyChanged(CellName(index));
            RefreshAscii();
        }
    }

    private byte ReadByte(int index) => _offset + index < _buffer.Length ? _buffer[_offset + index] : (byte)0;

    private static string CellName(int index) => index switch
    {
        0 => nameof(B0),
        1 => nameof(B1),
        2 => nameof(B2),
        3 => nameof(B3),
        4 => nameof(B4),
        5 => nameof(B5),
        6 => nameof(B6),
        7 => nameof(B7),
        8 => nameof(B8),
        9 => nameof(B9),
        10 => nameof(BA),
        11 => nameof(BB),
        12 => nameof(BC),
        13 => nameof(BD),
        14 => nameof(BE),
        _ => nameof(BF)
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


