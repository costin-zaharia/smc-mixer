using System.Windows;

namespace SmcMixer.App;

public partial class MainWindow : Window
{
    private readonly MixerVm _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MixerVm(Dispatcher);
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.Start();
        Closed += (_, _) => _viewModel.Dispose();
    }
}
