using RT809F.SDK;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var progress = new Progress<int>(value => Console.Error.Write($"\r{Math.Clamp(value, 0, 100),3}%"));

try
{
    var command = args[0].ToLowerInvariant();
    if (command == "detect")
    {
        Console.WriteLine(RT809FProgrammer.IsConnected() ? "RT809F connected" : "RT809F not found");
        return RT809FProgrammer.IsConnected() ? 0 : 2;
    }

    using var device = RT809FProgrammer.Open();
    switch (command)
    {
        case "id":
            Console.WriteLine(device.ReadId());
            break;
        case "read":
        {
            Require(args, 3, "read <output.bin> <length> [offset]");
            var length = Number(args[2]);
            var offset = args.Length > 3 ? Number(args[3]) : 0;
            var data = await device.ReadAsync((uint)offset, length, progress, cancellation.Token);
            await File.WriteAllBytesAsync(args[1], data, cancellation.Token);
            Console.Error.WriteLine();
            Console.WriteLine($"Saved {data.Length} bytes to {args[1]}");
            break;
        }
        case "blank":
        {
            Require(args, 2, "blank <length> [offset]");
            var length = Number(args[1]);
            var offset = args.Length > 2 ? Number(args[2]) : 0;
            await device.BlankCheckAsync((uint)offset, length, progress, cancellation.Token);
            Console.Error.WriteLine();
            Console.WriteLine("Blank check passed");
            break;
        }
        case "verify":
        {
            Require(args, 2, "verify <input.bin> [offset]");
            var data = await File.ReadAllBytesAsync(args[1], cancellation.Token);
            var offset = args.Length > 2 ? Number(args[2]) : 0;
            await device.VerifyAsync((uint)offset, data, progress, cancellation.Token);
            Console.Error.WriteLine();
            Console.WriteLine("Verify passed");
            break;
        }
        case "erase":
        {
            ConfirmDestructive(args);
            await device.EraseAsync(TimeSpan.FromMinutes(3), progress, cancellation.Token);
            Console.Error.WriteLine();
            Console.WriteLine("Erase completed");
            break;
        }
        case "write":
        {
            Require(args, 2, "write <input.bin> [offset] [--skip-ff] --yes");
            ConfirmDestructive(args);
            var data = await File.ReadAllBytesAsync(args[1], cancellation.Token);
            var offsetArg = args.Skip(2).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            var offset = offsetArg is null ? 0 : Number(offsetArg);
            await device.ProgramAsync((uint)offset, data, args.Contains("--skip-ff"), progress, cancellation.Token);
            Console.Error.WriteLine();
            Console.WriteLine("Write completed");
            break;
        }
        default:
            PrintUsage();
            return 1;
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("\nCancelled"); return 130; }
catch (Exception ex) { Console.Error.WriteLine($"\n{ex.Message}"); return 1; }

static int Number(string value) => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
    ? Convert.ToInt32(value[2..], 16) : int.Parse(value);
static void Require(string[] values, int count, string usage)
{
    if (values.Length < count) throw new ArgumentException($"Usage: rt809f {usage}");
}
static void ConfirmDestructive(string[] values)
{
    if (!values.Contains("--yes")) throw new ArgumentException("Destructive command requires --yes");
}
static void PrintUsage() => Console.WriteLine("""
RT809F CLI for Nexus Programmer
  rt809f detect
  rt809f id
  rt809f read <output.bin> <length> [offset]
  rt809f blank <length> [offset]
  rt809f verify <input.bin> [offset]
  rt809f erase --yes
  rt809f write <input.bin> [offset] [--skip-ff] --yes
Numbers accept decimal or 0x-prefixed hexadecimal notation.
""");
