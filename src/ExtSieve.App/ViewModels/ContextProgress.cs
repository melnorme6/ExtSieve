namespace ExtSieve.App.ViewModels;

internal sealed class ContextProgress<T>(Action<T> callback) : IProgress<T>
{
    private readonly Action<T> _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context is null || ReferenceEquals(_context, SynchronizationContext.Current))
        {
            _callback(value);
            return;
        }

        _context.Post(static state =>
        {
            var (progress, reportedValue) = ((ContextProgress<T>, T))state!;
            progress._callback(reportedValue);
        }, (this, value));
    }
}
