using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryApp.Data;
using LibraryApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace LibraryApp.ViewModels;

public partial class BookEditViewModel : ObservableObject
{
    private readonly int? _bookId;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _isbnValue = string.Empty;
    [ObservableProperty] private int _publishYear = DateTime.Now.Year;
    [ObservableProperty] private int _quantityInStock;
    [ObservableProperty] private Author? _selectedAuthor;
    [ObservableProperty] private Genre? _selectedGenre;
    [ObservableProperty] private ObservableCollection<Author> _authors = [];
    [ObservableProperty] private ObservableCollection<Genre> _genres = [];
    [ObservableProperty] private string _windowTitle = "Добавить книгу";

    public BookEditViewModel() { }

    public BookEditViewModel(int bookId)
    {
        _bookId = bookId;
        WindowTitle = "Редактировать книгу";
    }

    private async Task LoadBookAsync()
    {
        try
        {
            await using var context = new LibraryDbContext();
            var book = await context.Books
                .Include(b => b.Author)
                .Include(b => b.Genre)
                .FirstOrDefaultAsync(b => b.Id == _bookId);
            if (book is null) return;
            Title = book.Title;
            IsbnValue = book.ISBN;
            PublishYear = book.PublishYear;
            QuantityInStock = book.QuantityInStock;
            SelectedAuthor = Authors.FirstOrDefault(a => a.Id == book.AuthorId);
            SelectedGenre = Genres.FirstOrDefault(g => g.Id == book.GenreId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки книги:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task LoadLookupsAsync()
    {
        try
        {
            await using var context = new LibraryDbContext();
            var authorList = await context.Authors.OrderBy(a => a.LastName).ToListAsync();
            var genreList = await context.Genres.OrderBy(g => g.Name).ToListAsync();
            Authors = new ObservableCollection<Author>(authorList);
            Genres = new ObservableCollection<Genre>(genreList);

            if (_bookId.HasValue)
                await LoadBookAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки данных:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task Save(Window? window)
    {
        if (string.IsNullOrWhiteSpace(Title))
        { MessageBox.Show("Укажите название книги.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(IsbnValue))
        { MessageBox.Show("Укажите ISBN.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (SelectedAuthor is null)
        { MessageBox.Show("Выберите автора.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (SelectedGenre is null)
        { MessageBox.Show("Выберите жанр.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (PublishYear < 1 || PublishYear > DateTime.Now.Year)
        { MessageBox.Show("Укажите корректный год публикации.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (QuantityInStock < 0)
        { MessageBox.Show("Количество не может быть отрицательным.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        try
        {
            await using var context = new LibraryDbContext();
            if (_bookId.HasValue)
            {
                var book = await context.Books.FindAsync(_bookId.Value);
                if (book is null) return;
                book.Title = Title;
                book.ISBN = IsbnValue;
                book.PublishYear = PublishYear;
                book.QuantityInStock = QuantityInStock;
                book.AuthorId = SelectedAuthor.Id;
                book.GenreId = SelectedGenre.Id;
            }
            else
            {
                context.Books.Add(new Book
                {
                    Title = Title,
                    ISBN = IsbnValue,
                    PublishYear = PublishYear,
                    QuantityInStock = QuantityInStock,
                    AuthorId = SelectedAuthor.Id,
                    GenreId = SelectedGenre.Id
                });
            }
            await context.SaveChangesAsync();
            if (window is not null)
            {
                window.DialogResult = true;
                window.Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private static void Cancel(Window? window)
    {
        if (window is not null)
        {
            window.DialogResult = false;
            window.Close();
        }
    }
}