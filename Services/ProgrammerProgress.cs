namespace NexusProgrammer;

internal static class ProgrammerProgress
{
    public static int ProgressPercent(int done, int total) =>
        total <= 0 ? 100 : (int)Math.Clamp((long)done * 100 / total, 0, 100);
}
