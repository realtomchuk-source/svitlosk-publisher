using System;
using System.Text.RegularExpressions;

namespace SvitloSk.Publisher.Domain.Contracts;

public static class InputPackageValidator
{
    private static readonly Regex HhMmRegex = new Regex(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.Compiled);

    public static void Validate(TextInputPackageDto package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        
        if (package.Metadata == null) throw new ArgumentException("Metadata is missing");
        if (package.Metadata.PackageId == Guid.Empty) throw new ArgumentException("PackageId is missing or empty");
        // DateTimeOffset will be correctly parsed by JsonSerializer and default is checked here
        if (package.Metadata.GenerationTimestamp == default) throw new ArgumentException("GenerationTimestamp is missing or invalid");
        if (string.IsNullOrWhiteSpace(package.Metadata.SourceIdentifier)) throw new ArgumentException("SourceIdentifier is missing");
        if (!Enum.IsDefined(typeof(PackageState), package.Metadata.PackageState)) throw new ArgumentException("PackageState is missing or invalid");

        if (string.IsNullOrWhiteSpace(package.TerritorialScope)) throw new ArgumentException("TerritorialScope is missing");

        if (package.Events == null || package.Events.Count == 0) throw new ArgumentException("Events array is missing or empty");

        foreach (var ev in package.Events)
        {
            if (string.IsNullOrWhiteSpace(ev.Settlement)) throw new ArgumentException("Settlement is missing");
            if (ev.Streets == null || ev.Streets.Count == 0) throw new ArgumentException("Streets array is missing or empty");
            if (ev.Intervals == null || ev.Intervals.Count == 0) throw new ArgumentException("Intervals array is missing or empty");

            foreach (var interval in ev.Intervals)
            {
                if (interval.StartTime == default) throw new ArgumentException("StartTime is missing or invalid");
                if (interval.EndTime == default) throw new ArgumentException("EndTime is missing or invalid");
            }
        }
    }

    public static void Validate(GraphicInputPackageDto package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));

        if (package.Metadata == null) throw new ArgumentException("Metadata is missing");
        if (package.Metadata.PackageId == Guid.Empty) throw new ArgumentException("PackageId is missing or empty");
        if (package.Metadata.GenerationTimestamp == default) throw new ArgumentException("GenerationTimestamp is missing or invalid");
        if (package.Metadata.TargetDate == default) throw new ArgumentException("TargetDate is missing or invalid");
        if (string.IsNullOrWhiteSpace(package.Metadata.SourceIdentifier)) throw new ArgumentException("SourceIdentifier is missing");

        if (string.IsNullOrWhiteSpace(package.TerritorialScope)) throw new ArgumentException("TerritorialScope is missing");

        if (package.Queues == null || package.Queues.Count == 0) throw new ArgumentException("Queues array is missing or empty");

        foreach (var queue in package.Queues)
        {
            if (string.IsNullOrWhiteSpace(queue.QueueId)) throw new ArgumentException("QueueId is missing");
            if (queue.Subqueues == null || queue.Subqueues.Count == 0) throw new ArgumentException("Subqueues array is missing or empty");

            foreach (var subqueue in queue.Subqueues)
            {
                if (string.IsNullOrWhiteSpace(subqueue.SubqueueId)) throw new ArgumentException("SubqueueId is missing");
                if (subqueue.Intervals == null || subqueue.Intervals.Count == 0) throw new ArgumentException("Intervals array is missing or empty");

                foreach (var interval in subqueue.Intervals)
                {
                    if (string.IsNullOrWhiteSpace(interval.StartTime) || !HhMmRegex.IsMatch(interval.StartTime))
                        throw new ArgumentException("StartTime is missing or not in HH:MM format");
                    
                    if (string.IsNullOrWhiteSpace(interval.EndTime) || !HhMmRegex.IsMatch(interval.EndTime))
                        throw new ArgumentException("EndTime is missing or not in HH:MM format");

                    if (string.IsNullOrWhiteSpace(interval.Status)) throw new ArgumentException("Status is missing");
                }
            }
        }
    }
}
