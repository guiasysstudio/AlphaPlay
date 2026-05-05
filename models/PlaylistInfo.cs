namespace AlphaPlay.Models
{
    public class PlaylistInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool IsFileBased => !string.IsNullOrWhiteSpace(FilePath);

        public override string ToString()
        {
            return Name;
        }
    }
}
