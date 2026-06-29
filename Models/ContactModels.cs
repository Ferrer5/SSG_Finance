using System.Collections.Generic;

namespace MyMvcApp.Models;

// Public Contacts page: the treasurer(s) shown as contacts.
public class ContactViewModel
{
    public List<TreasurerContact> Treasurers { get; set; } = new();
}

public class TreasurerContact
{
    public string Name  { get; set; } = string.Empty;
    public string Role  { get; set; } = "Treasurer";
    public string Email { get; set; } = string.Empty;
}
