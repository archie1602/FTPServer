using System;
using Figgle;
using Pastel;

namespace ftpserver
{
    class Program
    {
        static void Main(string[] args)
        {
            // Ip FTP сервера
            string ip = (args.Length == 0) ? "127.0.0.1" : args[0];

            // Port FTP сервера
            int port = (args.Length < 2) ? 11000 : int.Parse(args[1]);

            // Рабочая директория FTP сервера
            string workingDirPath = (args.Length < 3) ? @"/home/archie/Desktop/MainDir/Desktop/FTPUsers/user1" : args[2];

            // Создаём FTP сервер
            Server FTP = new Server(ip, port, workingDirPath);

            Console.Clear();

            Console.WriteLine(FiggleFonts.Larry3d.Render("MyFTP 1.0").Pastel("#0ffd00"));
            Console.WriteLine("FTP server started successfully\n".Pastel("#0ffd00"));
            Console.WriteLine("Ip: ".Pastel("#e50000") + ip);
            Console.WriteLine("Port: ".Pastel("#e50000") + port.ToString());
            Console.WriteLine("Working directory: ".Pastel("#e50000") + workingDirPath);

            // Запускаем FTP сервер
            FTP.Execute();
        }
    }
}
