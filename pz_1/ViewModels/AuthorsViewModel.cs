using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryApp.Data;
using LibraryApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace LibraryApp.ViewModels;

public partial class AuthorsViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Author> _authors = [];
    [ObservableProperty] private Author? _selectedAuthor;

    // Edit fields
    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string _country = string.Empty;
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "Добавить автора";

    private int? _editingId;

    public AuthorsViewModel()
    {
        _ = LoadAsync();
    }

    public async Task LoadAsync()
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

    [RelayCommand]
    private void StartAdd()
    {
        _editingId = null;
        FirstName = string.Empty;
        LastName = string.Empty;
        Country = string.Empty;
        BirthDate = null;
        FormTitle = "Добавить автора";
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedAuthor is null) return;
        _editingId = SelectedAuthor.Id;
        FirstName = SelectedAuthor.FirstName;
        LastName = SelectedAuthor.LastName;
        Country = SelectedAuthor.Country ?? string.Empty;
        BirthDate = SelectedAuthor.BirthDate.HasValue
            ? SelectedAuthor.BirthDate.Value.ToDateTime(TimeOnly.MinValue)
            : null;
        FormTitle = "Редактировать автора";
        IsEditing = true;
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedAuthor is null) return;
        var result = MessageBox.Show(
            $"Удалить автора \"{SelectedAuthor.FullName}\"?\nВсе книги автора также будут удалены.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await using var context = new LibraryDbContext();
            var author = await context.Authors.FindAsync(SelectedAuthor.Id);
            if (author is not null)
            {
                context.Authors.Remove(author);
                await context.SaveChangesAsync();
            }
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ShowError("Ошибка удаления", ex);
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        { MessageBox.Show("Укажите имя.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(LastName))
        { MessageBox.Show("Укажите фамилию.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        try
        {
            await using var context = new LibraryDbContext();
            DateOnly? birthDate = BirthDate.HasValue ? DateOnly.FromDateTime(BirthDate.Value) : null;

            if (_editingId.HasValue)
            {
                var author = await context.Authors.FindAsync(_editingId.Value);
                if (author is null) return;
                author.FirstName = FirstName;
                author.LastName = LastName;
                author.Country = string.IsNullOrWhiteSpace(Country) ? null : Country;
                author.BirthDate = birthDate;
            }
            else
            {
                context.Authors.Add(new Author
                {
                    FirstName = FirstName,
                    LastName = LastName,
                    Country = string.IsNullOrWhiteSpace(Country) ? null : Country,
                    BirthDate = birthDate
                });
            }
            await context.SaveChangesAsync();
            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ShowError("Ошибка сохранения", ex);
        }
    }

    [RelayCommand]
    private void Cancel() => IsEditing = false;

    private static void ShowError(string msg, Exception ex)
        => MessageBox.Show($"{msg}:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
}
