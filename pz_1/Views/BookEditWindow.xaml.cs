using LibraryApp.ViewModels;
using System.Windows;

namespace LibraryApp.Views;

public partial class BookEditWindow : Window
{
    public BookEditWindow(BookEditViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
