namespace SelectionTranslator.Core.Interop;

public static class StaRunner
{
    public static Task<T> RunAsync<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static T Run<T>(Func<T> action) => RunAsync(action).GetAwaiter().GetResult();
}