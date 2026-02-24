namespace LibraryApp.Models;

public class Author
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly? BirthDate { get; set; }
    public string? Country { get; set; }

    public ICollection<Book> Books { get; set; } = new List<Book>();

    public string FullName => $"{LastName} {FirstName}";
}
