using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using RomM.Platforms.Abstractions.Models.Install;
using RomMbox.UI.Models;
using RomMbox.UI.ViewModels;
using RomMbox.UI.Views;

#nullable enable

namespace RomMbox.Services.PlatformInstallers
{
    internal static class PlatformInstallerUi
    {
        public static Task<ExecutableSelectionResult> SelectExecutableAsync(ExecutableSelectionRequest request)
        {
            if (request == null)
            {
                return Task.FromResult(new ExecutableSelectionResult { Confirmed = false });
            }

            if (Application.Current == null)
            {
                return Task.FromResult(new ExecutableSelectionResult
                {
                    Confirmed = false,
                    Selected = request.Recommended ?? request.Candidates?.FirstOrDefault()
                });
            }

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var rows = BuildExecutableCandidateRows(request.Candidates, request.Recommended).ToList();
                var recommendedRow = rows.FirstOrDefault(row => row.IsRecommended) ?? rows.FirstOrDefault();
                var viewModel = new ExecutableSelectionViewModel(
                    request.Title ?? "Executable Selection",
                    request.Message ?? "Multiple executables were detected. Choose the executable to launch.",
                    rows,
                    recommendedRow);

                var dialog = new ExecutableSelectionDialog
                {
                    Owner = Application.Current.MainWindow,
                    Topmost = true,
                    DataContext = viewModel
                };

                viewModel.RequestClose += confirmed =>
                {
                    dialog.DialogResult = confirmed;
                    dialog.Close();
                };

                dialog.ShowActivated = true;
                dialog.Activate();
                var result = dialog.ShowDialog();
                var selectedRow = viewModel.SelectedCandidate ?? recommendedRow;
                return new ExecutableSelectionResult
                {
                    Confirmed = result == true,
                    Selected = selectedRow == null
                        ? request.Recommended ?? request.Candidates?.FirstOrDefault()
                        : new ExecutableCandidate
                        {
                            FullPath = selectedRow.FullPath,
                            DisplayPath = selectedRow.DisplayPath,
                            FileName = selectedRow.FileName,
                            FileSizeBytes = selectedRow.FileSizeBytes,
                            FileSizeDisplay = selectedRow.FileSizeDisplay,
                            Version = selectedRow.Version,
                            Architecture = selectedRow.Architecture,
                            LastModifiedDisplay = selectedRow.LastModifiedDisplay,
                            IsRecommended = selectedRow.IsRecommended
                        }
                };
            }).Task;
        }

        public static Task<bool> ConfirmAsync(ConfirmationRequest request)
        {
            if (request == null)
            {
                return Task.FromResult(false);
            }

            if (Application.Current == null)
            {
                var result = MessageBox.Show($"{request.Message}\n{request.Detail}", request.Title ?? "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                return Task.FromResult(result == MessageBoxResult.Yes);
            }

            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmDialog(request.Title ?? "Confirm", request.Message ?? string.Empty, request.Detail ?? string.Empty)
                {
                    Owner = Application.Current.MainWindow,
                    Topmost = true
                };
                dialog.ShowActivated = true;
                dialog.Activate();
                var result = dialog.ShowDialog();
                return result == true;
            }).Task;
        }

        private static IEnumerable<ExecutableCandidateRow> BuildExecutableCandidateRows(
            IReadOnlyList<ExecutableCandidate>? candidates,
            ExecutableCandidate? recommended)
        {
            if (candidates == null)
            {
                yield break;
            }

            foreach (var candidate in candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.FullPath))
                {
                    continue;
                }

                var isRecommended = !string.IsNullOrWhiteSpace(recommended?.FullPath)
                    && string.Equals(candidate.FullPath, recommended.FullPath, StringComparison.OrdinalIgnoreCase);

                yield return new ExecutableCandidateRow
                {
                    IsRecommended = isRecommended,
                    FileName = candidate.FileName ?? System.IO.Path.GetFileName(candidate.FullPath) ?? candidate.FullPath,
                    FullPath = candidate.FullPath,
                    DisplayPath = candidate.DisplayPath ?? candidate.FullPath,
                    FileSizeBytes = candidate.FileSizeBytes,
                    FileSizeDisplay = candidate.FileSizeDisplay ?? string.Empty,
                    Version = candidate.Version ?? string.Empty,
                    LastModifiedDisplay = candidate.LastModifiedDisplay ?? string.Empty,
                    Architecture = candidate.Architecture ?? string.Empty
                };
            }
        }
    }
}
