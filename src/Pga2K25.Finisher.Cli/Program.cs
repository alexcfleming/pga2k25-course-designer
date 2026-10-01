using Pga2K25.Finisher;

// Standalone command line for the Course Finisher port (the same commands as `pga2k25 finish / analyze`).
if (args.Length < 2 || args[0] is not ("finish" or "analyze"))
{
    Console.Error.WriteLine("usage: course-finisher " + FinisherCommandLine.Usage);
    return 2;
}
try
{
    if (args[0] == "analyze") Console.WriteLine(CourseFinisher.Analyze(args[1]).Summary());
    else FinisherCommandLine.Run(args[1], args[2..], new SyncProgress(Console.WriteLine));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(Environment.GetEnvironmentVariable("PGA2K25_DEBUG") is null ? $"error: {ex.Message}" : ex.ToString());
    return 1;
}

sealed class SyncProgress(Action<string> write) : IProgress<string>
{
    public void Report(string value) => write(value);
}
