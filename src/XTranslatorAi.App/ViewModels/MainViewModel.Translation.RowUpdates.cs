using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private Task OnRowUpdatedAsync(long id, StringEntryStatus status, string text)
    {
        _rowUpdates.Enqueue(new RowUpdate(id, status, text));
        EnsureRowUpdatePump();
        return Task.CompletedTask;
    }

    private void EnsureRowUpdatePump()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            if (Interlocked.Exchange(ref _rowUpdatePumpScheduled, 1) == 1)
            {
                return;
            }

            dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        Interlocked.Exchange(ref _rowUpdatePumpScheduled, 0);
                        EnsureRowUpdatePump();
                    }
                ),
                DispatcherPriority.Background
            );

            return;
        }

        if (_rowUpdateTimer == null)
        {
            _rowUpdateTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(40),
            };
            _rowUpdateTimer.Tick += (_, _) => DrainRowUpdates();
        }

        if (!_rowUpdateTimer.IsEnabled)
        {
            _rowUpdateTimer.Start();
        }
    }

    private void DrainRowUpdates()
    {
        var processed = 0;
        while (processed < 300 && _rowUpdates.TryDequeue(out var update))
        {
            ApplyRowUpdate(update);
            processed++;
        }

        if (_rowUpdates.IsEmpty)
        {
            _rowUpdateTimer?.Stop();
        }
    }

    private void ApplyRowUpdate(RowUpdate update)
    {
        if (_projectState.TryGetById(update.Id, out var vm))
        {
            var previous = vm.Status;
            vm.Status = update.Status;
            if (update.Status == StringEntryStatus.InProgress)
            {
                _inProgressSinceTranslationStart.Add(update.Id);
                vm.IsTranslationMemoryApplied = false;
            }
            if (update.Status == StringEntryStatus.Done)
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    vm.DestText = update.Text;
                }
                vm.IsTranslationMemoryApplied = !_inProgressSinceTranslationStart.Contains(update.Id);
                if (previous != StringEntryStatus.Done)
                {
                    DoneCount++;
                    PendingCount = Math.Max(0, PendingCount - 1);
                }
            }
            if (update.Status == StringEntryStatus.Error)
            {
                var raw = string.IsNullOrWhiteSpace(update.Text) ? "Error" : update.Text;
                vm.ErrorMessage = raw;

                if (previous != StringEntryStatus.Error)
                {
                    var classified = UserFacingErrorClassifier.ClassifyErrorMessage(raw);
                    AppLog.Write(
                        $"ROW_ERROR {classified.Code} id={vm.Id} edid={vm.Edid ?? ""} rec={vm.Rec ?? ""} msg={Truncate(raw, 600)}"
                    );
                }
            }
        }
    }

    private static string Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
        {
            return text ?? "";
        }

        return text[..maxLen] + "…";
    }

    private readonly record struct RowUpdate(long Id, StringEntryStatus Status, string Text);
}
