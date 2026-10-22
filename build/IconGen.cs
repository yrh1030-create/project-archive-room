namespace DangAn
{
    static class IconGen
    {
        static void Main(string[] args)
        {
            AppIcon.SaveIco(args[0]);
            if (args.Length > 1) using (var b = AppIcon.Render(256)) b.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
