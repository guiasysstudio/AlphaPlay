namespace AlphaPlay.Models
{
    public class PlaylistInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public override string ToString()
        {
            return Name;
        }
    }
}
