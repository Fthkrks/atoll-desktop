using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HyTranslate.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace HyTranslate.App.Views;

public sealed partial class DocumentTranslatePage : Page
{
    public DocumentViewModel ViewModel { get; }

    public DocumentTranslatePage()
    {
        ViewModel = App.Services.GetRequiredService<DocumentViewModel>();
        this.InitializeComponent();
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };

        picker.FileTypeFilter.Add(".docx");
        picker.FileTypeFilter.Add(".pptx");
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".pdf");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.SetFile(file.Path);
        }
    }

    private void ClearSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedFilePath = null;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Dosyayı buraya bırakın";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
            {
                var allowedExts = new[] { ".docx", ".pptx", ".xlsx", ".pdf", ".txt", ".md" };
                if (allowedExts.Contains(file.FileType.ToLowerInvariant()))
                {
                    ViewModel.SetFile(file.Path);
                }
            }
        }
    }

    private async void ChangeOutputPath_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.SelectedFilePath)) return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        var currentPath = ViewModel.OutputFilePath ?? ViewModel.SelectedFilePath;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(currentPath);
        picker.SuggestedFileName = fileNameWithoutExt;

        var ext = Path.GetExtension(ViewModel.SelectedFilePath)?.ToLowerInvariant() ?? ".pdf";
        var desc = ext switch
        {
            ".pdf" => "PDF belgesi",
            ".docx" => "Word belgesi",
            ".pptx" => "PowerPoint sunumu",
            ".xlsx" => "Excel çalışma kitabı",
            ".txt" => "Metin belgesi",
            ".md" => "Markdown belgesi",
            _ => $"{ext.TrimStart('.').ToUpperInvariant()} dosyası"
        };
        picker.FileTypeChoices.Add(desc, new List<string> { ext });

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            ViewModel.SetOutputPath(file.Path);
        }
    }
}
