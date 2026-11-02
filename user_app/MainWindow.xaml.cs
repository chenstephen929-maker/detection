using System.Windows;
using System.Windows.Controls;
using AirGroundControlApp.ViewModels;

namespace AirGroundControlApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void DronePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Drone.Password = ((PasswordBox)sender).Password;
    }

    private void DroneSudoPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Drone.SudoPassword = ((PasswordBox)sender).Password;
    }

    private void CarPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Car.Password = ((PasswordBox)sender).Password;
    }

    private void CarSudoPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Car.SudoPassword = ((PasswordBox)sender).Password;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
