namespace LOCKnet.Data.Models;

public class Credential
{
	public int Id { get; set; }
	public string Title { get; set; }
	public string Username { get; set; }
	public byte[] EncryptedPassword { get; set; }
	public string URL { get; set; }
	public string Notes { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}
