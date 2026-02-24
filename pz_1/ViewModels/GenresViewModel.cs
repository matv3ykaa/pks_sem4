using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibraryApp.Data;
using LibraryApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;

namespace LibraryApp.ViewModels;

public partial class GenresViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Genre> _genres = [];
    [ObservableProperty] private Genre? _selectedGenre;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _formTitle = "Добавить жанр";

    private int? _editingId;

    public GenresViewModel()
    {
        _ = LoadAsync();
    }

    public async Task LoadAsync()
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

    [RelayCommand]
    private void StartAdd()
    {
        _editingId = null;
        Name = string.Empty;
        Description = string.Empty;
        FormTitle = "Добавить жанр";
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedGenre is null) return;
        _editingId = SelectedGenre.Id;
        Name = SelectedGenre.Name;
        Description = SelectedGenre.Description ?? string.Empty;
        FormTitle = "Редактировать жанр";
        IsEditing = true;
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedGenre is null) return;
        var result = MessageBox.Show(
            $"Удалить жанр \"{SelectedGenre.Name}\"?\nВсе книги этого жанра также будут удалены.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await using var context = new LibraryDbContext();
            var genre = await context.Genres.FindAsync(SelectedGenre.Id);
            if (genre is not null)
            {
                context.Genres.Remove(genre);
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
        if (string.IsNullOrWhiteSpace(Name))
        { MessageBox.Show("Укажите название жанра.", "Валидация", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        try
        {
            await using var context = new LibraryDbContext();
            if (_editingId.HasValue)
            {
                var genre = await context.Genres.FindAsync(_editingId.Value);
                if (genre is null) return;
                genre.Name = Name;
                genre.Description = string.IsNullOrWhiteSpace(Description) ? null : Description;
            }
            else
            {
                context.Genres.Add(new Genre
                {
                    Name = Name,
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description
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
