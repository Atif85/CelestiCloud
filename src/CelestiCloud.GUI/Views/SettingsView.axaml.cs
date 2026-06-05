using Avalonia.Controls;
using Avalonia.Interactivity;
using CelestiCloud.GUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace CelestiCloud.GUI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void TextBox_LostFocus(object? sender, Avalonia.Input.FocusChangingEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.FormatInputOnLostFocus();
        }
    }
}
