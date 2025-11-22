namespace LOCKnet.Data.Models;

public class MasterKey
{
	public int Id { get; set; }
	public byte[] PasswordHash { get; set; }
	public byte[] Salt { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}
