using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IApiLogsTabHost : INotifyPropertyChanged
{
    bool EnableApiCallLogging { get; set; }
    IRelayCommand ClearApiCallLogsCommand { get; }

    string ApiCallLogTotalsSummary { get; }
    string ApiCallLogTotalsToolTip { get; }

    ObservableRangeCollection<ApiCallLogRow> ApiCallLogs { get; }
}
