using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Views;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace LibraryApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Book> _books = [];

    [ObservableProperty]
    private ObservableCollection<Author> _authors = [];

    [ObservableProperty]
    private ObservableCollection<Genre> _genres = [];

    [ObservableProperty]
    private Book? _selectedBook;

    [ObservableProperty]
    private Author? _filterAuthor;

    [ObservableProperty]
    private Genre? _filterGenre;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalBooks;

    partial void OnFilterAuthorChanged(Author? value) => _ = LoadBooksAsync();
    partial void OnFilterGenreChanged(Genre? value) => _ = LoadBooksAsync();
    partial void OnSearchTextChanged(string value) => _ = LoadBooksAsync();

    public MainViewModel()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await using var context = new LibraryDbContext();
        await context.Database.EnsureCreatedAsync();
        await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        await LoadAuthorsAsync();
        await LoadGenresAsync();
        await LoadBooksAsync();
    }

    private async Task LoadAuthorsAsync()
    {
        try
        {
            await using var context = new LibraryDbContext();
            var list = await context.Authors.OrderBy(a => a.LastName).ToListAsync();
            Authors = new ObservableCollection<Author>(list);
        }
        catch (Exception ex)
        {
            ShowError("Ошибка загрузки авторов", ex);
        }
    }

    private async Task LoadGenresAsync()
    {
        try
        {
            await using var context = new LibraryDbContext();
            var list = await context.Genres.OrderBy(g => g.Name).ToListAsync();
            Genres = new ObservableCollection<Genre>(list);
        }
        catch (Exception ex)
        {
            ShowError("Ошибка загрузки жанров", ex);
        }
    }

    public async Task LoadBooksAsync()
    {
        try
        {
            await using var context = new LibraryDbContext();
            IQueryable<Book> query = context.Books
                .Include(b => b.Author)
                .Include(b => b.Genre);

            if (FilterAuthor is not null)
                query = query.Where(b => b.AuthorId == FilterAuthor.Id);

            if (FilterGenre is not null)
                query = query.Where(b => b.GenreId == FilterGenre.Id);

            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(b => b.Title.Contains(SearchText));

            var list = await query.OrderBy(b => b.Title).ToListAsync();
            Books = new ObservableCollection<Book>(list);
            TotalBooks = list.Sum(b => b.QuantityInStock);
        }
        catch (Exception ex)
        {
            ShowError("Ошибка загрузки книг", ex);
        }
    }

    [RelayCommand]
    private async Task AddBook()
    {
        var vm = new BookEditViewModel();
        await vm.LoadLookupsAsync();
        var dialog = new BookEditWindow(vm);
        if (dialog.ShowDialog() == true)
            await LoadBooksAsync();
    }

    [RelayCommand]
    private async Task EditBook()
    {
        if (SelectedBook is null) return;
        var vm = new BookEditViewModel(SelectedBook.Id);
        await vm.LoadLookupsAsync();
        var dialog = new BookEditWindow(vm);
        if (dialog.ShowDialog() == true)
            await LoadBooksAsync();
    }

    [RelayCommand]
    private async Task DeleteBook()
    {
        if (SelectedBook is null) return;
        var result = MessageBox.Show(
            $"Удалить книгу \"{SelectedBook.Title}\"?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await using var context = new LibraryDbContext();
            var book = await context.Books.FindAsync(SelectedBook.Id);
            if (book is not null)
            {
                context.Books.Remove(book);
                await context.SaveChangesAsync();
            }
            await LoadBooksAsync();
        }
        catch (Exception ex)
        {
            ShowError("Ошибка удаления книги", ex);
        }
    }

    [RelayCommand]
    private async Task ManageAuthors()
    {
        var dialog = new AuthorsWindow();
        dialog.ShowDialog();
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task ManageGenres()
    {
        var dialog = new GenresWindow();
        dialog.ShowDialog();
        await LoadDataAsync();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterAuthor = null;
        FilterGenre = null;
        SearchText = string.Empty;
    }

    private static void ShowError(string message, Exception ex)
        => MessageBox.Show($"{message}:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
}
