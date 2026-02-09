using System;
using System.Threading.Tasks;
using System.Windows;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private static Task DispatchAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static Task DispatchAsync(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}

