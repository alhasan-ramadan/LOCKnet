namespace LOCKnet.Data.Models;

public class Setting
{
	public int Id { get; set; }
	public string Key { get; set; }
	public string Value { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}
