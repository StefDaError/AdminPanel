using System.Windows;
using AdminPanel.ViewModels;

namespace AdminPanel;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Передаем созданную базу в ViewModel
        DataContext = new AdminViewModel(App.Database);
    }
}