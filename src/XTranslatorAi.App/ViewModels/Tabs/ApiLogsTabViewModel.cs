using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class ApiLogsTabViewModel : ObservableObject
{
    private readonly IApiLogsTabHost _host;

    public ApiLogsTabViewModel(IApiLogsTabHost host)
    {
        _host = host;
        _host.PropertyChanged += HostOnPropertyChanged;
    }

    private void HostOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            OnPropertyChanged(string.Empty);
            return;
        }

        OnPropertyChanged(e.PropertyName);
    }

    public bool EnableApiCallLogging
    {
        get => _host.EnableApiCallLogging;
        set => _host.EnableApiCallLogging = value;
    }

    public IRelayCommand ClearApiCallLogsCommand => _host.ClearApiCallLogsCommand;

    public string ApiCallLogTotalsSummary => _host.ApiCallLogTotalsSummary;
    public string ApiCallLogTotalsToolTip => _host.ApiCallLogTotalsToolTip;

    public ObservableRangeCollection<ApiCallLogRow> ApiCallLogs => _host.ApiCallLogs;
}
