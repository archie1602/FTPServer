using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Mono.Unix;
using System.Collections.Generic;

namespace ftpserver
{
    // Класс - клиент, который описывает состояние подключения конкретного ftp-клиента
    // и содержит всю информацию, связанную с данным клиентом
    public class Client
    {
        // Поля класса

        // Client control connection socket
        Socket cltCtrlConnSock;

        // Listener data connection socket
        ConnectedSocket lstrDataConnSock;

        // Client data connection socket
        Socket cltDataConnSock;

        // Recent unhandled command
        (Cmd, string, long?)? unhandledCmd;

        // Recent unhandled response
        (Cmd?, byte[], int, bool, string)? unhandledRes;

        // Id клиента
        static long id = 0;
        long idCurrent = 0;

        FileStream fstream;
        string currDir;
        bool isLogGet = false;
        bool isAuth = false;
        bool isPassiveOn = false;
        bool isCtrlConnBlocked = false;
        IPAddress serverIP;

        // Путь к рабочей директории пользователя
        string workingDirPath;

        // Конструктор класса
        public Client(Socket _cltCtrlConnSock, ConnectedSocket _lstrDataConnSock, IPAddress _serverIP, string _workingDirPath)
        {
            cltCtrlConnSock = _cltCtrlConnSock;
            lstrDataConnSock = _lstrDataConnSock;
            cltDataConnSock = null;
            currDir = "/";
            workingDirPath = _workingDirPath;
            serverIP = _serverIP;
            unhandledCmd = null;
            unhandledRes = null;
            fstream = null;
            idCurrent = id++;
        }

        // Get и Set свойства
        public Socket CltCtrlConnSock
        {
            get { return cltCtrlConnSock; }
            set { cltCtrlConnSock = value; }
        }

        public ConnectedSocket LstrDataConnSock
        {
            get { return lstrDataConnSock; }
            set { lstrDataConnSock = value; }
        }

        public Socket CltDataConnSock
        {
            get { return cltDataConnSock; }
            set { cltDataConnSock = value; }
        }

        public (Cmd, string, long?)? UnhandledCmd
        {
            get { return unhandledCmd; }
            set { unhandledCmd = value; }
        }

        public Cmd? RecentUnhandledCmd
        {
            get { return (unhandledCmd == null) ? null : unhandledCmd.Value.Item1; }
        }

        public (Cmd?, byte[], int, bool, string)? RecentUnhandledResponse
        {
            get { return unhandledRes; }
        }

        public string CurrDir
        {
            get { return currDir; }
            set { currDir = value; }
        }

        public bool IsPassiveOn
        {
            get { return isPassiveOn; }
            set { isPassiveOn = value; }
        }

        public bool IsCtrlConnBlocked
        {
            get { return isCtrlConnBlocked; }
            set { isCtrlConnBlocked = value; }
        }

        public long GetId
        {
            get { return idCurrent; }
        }

        // Метод, который отправляет клиенту отклик с кодом 425 и требованием открыть соединение для передачи данных
        // прежде чем вызывать соответствующую команду, требуюущую данное соединение
        bool RequireOpenDataConnection(Cmd? cmd)
        {
            return SendResponse(cmd, "425 Use PORT or PASV first.");
        }

        // Метод, который вычисляет новый путь директории от домашнеей директории '/'
        // на основании текущего положения - 'currentPath'
        string SimplifyPath(string path, string currentPath)
        {
            // Если необходимо преобразовать путь ведущий к корневому или домашнему каталогу
            if (path == "/" || path == "~")
                return "/"; // Тогда: ничего не преоабразовываем и возвращает корневой каталог

            // Если необходимо преобразовать абсолютный путь к каталогу
            if (path[0] == '/') // Тогда: сводим абсолютный путь к относительно: убираем из начала строки path символ '/', а currentPath заменяем на '/'
                return SimplifyPath(path.Substring(1), "/"); // то есть работаем с абсолютным как с относительным, только от корневого каталога

            // Если указанный путь содержит в конце символ '/'
            if (path[path.Length - 1] == '/') // Тогда: для однозначности уберём его
                path = path.Substring(0, path.Length - 1); // Убираем из конца строки path символ '/'

            // Разбиваем path по '/'
            string[] pathArr = path.Split('/');

            // Результирующий путь к директории
            List<string> resultPath = null;

            // Если текущая директория является корневой
            if (currentPath == "/") // Тогда: просто создаём список и ничего в него не добавляем
                resultPath = new List<string>();
            else // Иначе
            {
                // Тогда:

                // Создаём список на основе массива currentPath.Split('/')
                resultPath = new List<string>(currentPath.Split('/'));

                // Удаляем из начала списка пустую строку
                resultPath.RemoveAt(0);
            }

            // Индекс уровня углублённости в пути resultPath
            int level = resultPath.Count;

            // Обходим массив pathArr, который необходимо преобразовать в более простой путь
            for (int i = 0; i < pathArr.Length; i++)
            {
                // Если необходимо подняться на уровень выше
                if (pathArr[i].Contains(".."))
                {
                    // Тогда: необходимо проверить не пытаемся ли выйти за пределы корневой директории

                    // Если уровень пути resultPath > 0
                    if (level > 0)
                    {
                        // Тогда:

                        // Удаляем из пути resultPath, директорию в которой сейчас находимся
                        // то есть поднимаемся на уровень выше
                        resultPath.RemoveAt(level - 1);

                        // Уменьшаем значение углублённости уровня на единицу
                        level--;
                    }
                }
                else // Иначе, если необходимо спуститься на уровень ниже
                {
                    // Тогда:

                    // Добавляем к пути resultPath ещё одну директорию
                    resultPath.Add(pathArr[i]);

                    // Увеличиваем значение углублённости уровня на единицу
                    level++;
                }
            }

            // Если уровень углублённости оказался равен 0
            // => результатом является путь к корневой директории
            // иначе записываем пустую строку, а цикл foreach составит нужный путь
            string resultPathStr = (level == 0) ? "/" : string.Empty;

            // Составляем получившbйся путь на основе списка resultPath
            foreach (string str in resultPath)
                resultPathStr += "/" + str;

            // Возвращаем полученный, упрощённый путь
            return resultPathStr;
        }

        // Метод, который конвертирует права доступа из целочисленного представления в строковое
        string getStrPerm(int numPerm, bool isDir = false)
        {
            char[] perm = (new string("----------")).ToCharArray();
            char[] mode = new char[] { 'x', 'w', 'r' };
            int j = 0;

            while (numPerm > 0)
            {
                if (numPerm % 2 == 1)
                    perm[9 - j] = mode[j % 3];

                numPerm /= 2;
                j++;
            }

            if (isDir)
                perm[0] = 'd';

            return new string(perm);
        }

        // Перечисление, содержащее FTP команды, как для передачи по управляющему соединению, так и для передачи по соединению для передачи данных
        public enum Cmd
        {
            // Листинг файлов
            LIST,
            // Скачивание файла с сервера
            RETR,
            // Загрузка файла на сервер
            STOR,
            USER,
            PASS,
            SYST,
            PWD,
            CWD,
            MKD,
            DELE,
            PASV,
            TYPE,
            QUIT
        }

        // Метод, который проверяет корректность заданного пути: path
        // для соответствующей команды: cmd
        (bool, string?) isPathCorrect(string path, Cmd cmd)
        {
            // Если необходимо выполнить проверку пути для команды STOR
            if (cmd == Cmd.STOR)
            {
                // Если указанный путь не ведёт к файлу
                if (path == null || path == "." || path == "/" || path == "~")
                    return (false, null);

                // Разбиваем путь к файлу по '/'
                string[] splitPath = path.Split('/');

                // Если в конце пути содержится '/' вместо имени файла
                if (splitPath[splitPath.Length - 1] == "")
                    return (false, null);

                // Получаем абсолютный путь к файлу
                string absPath = workingDirPath + SimplifyPath(path, currDir);

                // Если указанный путь к файлу не существует
                if (!Directory.GetParent(absPath).Exists)
                    return (false, null);

                // Создаём объект типа UnixFileInfo
                UnixFileInfo fileInfo = new UnixFileInfo(absPath);

                // Если указанный файл или директория уже существуют
                if (fileInfo.Exists)
                    return (false, null);

                // Возвращаем абсолютный путь к файлу
                return (true, absPath);
            } // Иначе, если необходимо выполнить проверку пути для команды RETR
            else if (cmd == Cmd.RETR)
            {
                // Если указанный путь не ведёт к файлу
                if (path == null || path == "." || path == "/" || path == "~")
                    return (false, null);

                // Разбиваем путь к файлу по '/'
                string[] splitPath = path.Split('/');

                // Если в конце пути содержится '/' вместо имени файла
                if (splitPath[splitPath.Length - 1] == "")
                    return (false, null);

                // Получаем абсолютный путь к файлу
                string absPath = workingDirPath + SimplifyPath(path, currDir);

                // Создаём объект типа UnixFileInfo
                UnixFileInfo fileInfo = new UnixFileInfo(absPath);

                // Если указанного файла не существует или он является директорией
                if (!fileInfo.Exists || (fileInfo.FileType == FileTypes.Directory))
                    return (false, null);

                // Возвращаем абсолютный путь к файлу
                return (true, absPath);
            }

            return (true, null);
        }

        // Метод, который удаляет заданый сокет из заданного списка, сравнивая по Socket.SafeHandle
        public bool RemoveSockFromList(List<ConnectedSocket> list, Socket sock)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].SafeHandle.Equals(sock.SafeHandle))
                {
                    list.RemoveAt(i);

                    return true;
                }
            }

            return false;
        }

        // Метод, который закрывает соединение для передачи данных
        public void CloseDataConnection(List<ConnectedSocket> connectedSockets)
        {
            // Удаляем из списка connectedSockets сокет cltDataConnSock
            RemoveSockFromList(connectedSockets, cltDataConnSock);

            // Запрещаем операции Both - отправки и получения данных на сокете клиента
            cltDataConnSock.Shutdown(SocketShutdown.Both);

            // Закрываем сокет клиента
            cltDataConnSock.Close();

            // Записываем в данный сокет null
            cltDataConnSock = null;

            // Запрещаем операции Both - отправки и получения данных на сокете для прослушивания
            lstrDataConnSock.Shutdown(SocketShutdown.Both);

            // Закрываем сокет для прослушивания
            lstrDataConnSock.Close();

            // Записываем в данный сокет null
            lstrDataConnSock = null;

            // Переводим flag: isPassiveOn в состояние: false
            isPassiveOn = false;

            // Сбрасываем последнюю необработанную команду
            unhandledCmd = null;

            // Осуществляем разблокировку обработки команд в управляющем соединении для данного клиента
            isCtrlConnBlocked = false;
        }

        // Метод, который обрабатывает команды соединения передачи данных
        public bool HandleDataConnection(Cmd cmd, string arg, List<ConnectedSocket> connectedSockets)
        {
            // Если клиент не подключился к соединению для передачи данных
            if (cltDataConnSock == null)
            {
                // Тогда: выполнение команды cmd придётся отложить и вернуться к ней,
                // когда клиент подключится к соединению для передачи данных;
                // также нужно заблокировать выполнение команд через управляющее соединение
                // для данного клиента

                // Сохраняем необработанную команду и её аргумент в поле unhandledCmd
                unhandledCmd = (cmd, arg, null);

                // Блокируем для данного клиента обработку команд в управляющем соединении
                isCtrlConnBlocked = true;
            }
            else // Иначе, если клиент подключился к соединению для передачи данных
            {
                // Тогда: обрабатываем данную команду

                // Если необходимо выполнить листинг
                if (cmd == Cmd.LIST)
                {
                    // Тогда: необходимо определить листинг файла или листинг директории нужно выполнить

                    bool isContinue = (unhandledCmd != null) && (unhandledCmd.Value.Item3 != null);

                    bool isExist = true;

                    // Строка для листинга
                    string listing = string.Empty;

                    // Если это первая попытка данного листинга
                    if (!isContinue)
                    {
                        // Удаляем из списка connectedSockets listener-сокет lstrDataConnSock
                        connectedSockets.Remove(lstrDataConnSock);

                        // Получаем абсолютный путь к директории или файлу
                        string absPath = workingDirPath + ((arg == null || arg == ".") ? currDir : SimplifyPath(arg, currDir));

                        // Создаём объект UnixFileInfo на основе пути absPath
                        UnixFileInfo currentFileOrDir = new UnixFileInfo(absPath);

                        // Переменная flag
                        isExist = currentFileOrDir.Exists;

                        // Если указанный файл или директория существует
                        if (isExist)
                        {
                            // Тогда: осуществляем листинг

                            // Если необходимо выполнить листинг директории
                            if (currentFileOrDir.FileType == FileTypes.Directory)
                            {
                                // Получаем массив строк всех директорий в указанной директории
                                string[] dirsPaths = Directory.GetDirectories(absPath);

                                // Получаем массив строк всех файлов в указанной директории
                                string[] filesPaths = Directory.GetFiles(absPath);

                                // Выделяем память под массив строк - все директории и файлы
                                string[] dirsAndFilesPaths = new string[dirsPaths.Length + filesPaths.Length];

                                // Соединяем два массива в один
                                dirsPaths.CopyTo(dirsAndFilesPaths, 0);
                                filesPaths.CopyTo(dirsAndFilesPaths, dirsPaths.Length);

                                // Информацию о текущем файле или директории
                                UnixFileInfo unixFileInfo;

                                for (int i = 0; i < dirsAndFilesPaths.Length; i++)
                                {
                                    // Получаем информацию о текущем файле или директории
                                    unixFileInfo = new UnixFileInfo(dirsAndFilesPaths[i]);

                                    // Вычисляем дату
                                    string date = unixFileInfo.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ? unixFileInfo.LastWriteTime.ToString("MMM dd  yyyy") : unixFileInfo.LastWriteTime.ToString("MMM dd HH:mm");

                                    // Добавляем очередной файл или директорию в строку ответа клиенту
                                    listing += string.Format("{0}    {1} {2}     {3}     {4,8} {5} {6}\r\n", getStrPerm((int)unixFileInfo.FileAccessPermissions, unixFileInfo.FileType == FileTypes.Directory), unixFileInfo.LinkCount.ToString(), unixFileInfo.OwnerUserId, unixFileInfo.OwnerGroupId, unixFileInfo.Length, date, unixFileInfo.Name);
                                }
                            }
                            else // Иначе, если необходимо выполнить листинг всего остального
                            {
                                // Вычисляем дату
                                string date = currentFileOrDir.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180) ? currentFileOrDir.LastWriteTime.ToString("MMM dd  yyyy") : currentFileOrDir.LastWriteTime.ToString("MMM dd HH:mm");

                                // Результат листинга
                                listing = string.Format("{0}    {1} {2}     {3}     {4,8} {5} {6}\r\n", getStrPerm((int)currentFileOrDir.FileAccessPermissions, currentFileOrDir.FileType == FileTypes.Directory), currentFileOrDir.LinkCount.ToString(), currentFileOrDir.OwnerUserId, currentFileOrDir.OwnerGroupId, currentFileOrDir.Length, date, currentFileOrDir.Name);
                            }

                        }

                        // Отправляем код 150 клиенту, который сигнализирует о том, что сейчас будет произведен листинг
                        if (!SendResponse(Cmd.LIST, "150 Here comes the directory listing.", true, arg)) return (unhandledRes == null) ? false : true;
                    }
                    else
                        listing = unhandledCmd.Value.Item2;

                    // Если указанный файл или директория существует
                    if (isExist)
                    {
                        // Тогда:

                        // Общее количество отправленных байт
                        int totalBytesSent = isContinue ? (int)unhandledCmd.Value.Item3.Value : 0;

                        byte[] buffer = Encoding.ASCII.GetBytes(listing);

                        // Отправляем листинг клиенту
                        totalBytesSent += cltDataConnSock.Send(buffer, totalBytesSent, buffer.Length - totalBytesSent, SocketFlags.None);

                        // Если отправилось меньше байт, чем требуется
                        if (totalBytesSent < buffer.Length)
                        {
                            // Тогда: => буфер сокета переполнен и необходимо продолжить отправку позже

                            unhandledCmd = (cmd, listing, totalBytesSent);

                            // Если это первая попытка отправить листинг
                            if (!isContinue)
                            {
                                // Тогда:

                                // Блокируем для данного клиента обработку команд в управляющем соединении
                                isCtrlConnBlocked = true;

                                // Добавляем сокет cltDataConnSock в список connectedSockets, чтобы
                                // отслеживать его состояние и когда данный сокет станет доступным для записи
                                // вернуться и продолжить листинг
                                connectedSockets.Add(new ConnectedSocket(cltDataConnSock, this, false, false, false, true));
                            }
                        }
                        else
                        {
                            // Отправляем клиенту код 226 - листинг успешно выполнен - в управляющее соединение
                            if (!SendResponse(Cmd.LIST, "226 Directory send OK.", true)) return (unhandledRes == null) ? false : true;
                        }
                    }
                    else // Иначе, если указанного файла или директории не существует
                    {
                        // Тогда: отправляем клиенту отклик код 450 с сообщением об ошибке: запрошенное действие с файлом не выполнено
                        if (!SendResponse(Cmd.LIST, "450 Requested file action not taken.") && unhandledRes == null) return false;
                    }

                    // Закрываем соединение для передачи данных
                    CloseDataConnection(connectedSockets);

                } // Иначе, если необходимо выполнить скачивание файла с сервера
                else if (cmd == Cmd.RETR)
                {
                    // Тогда:

                    // Если необходимо продолжить отправку данного файла, то isContinue = true, иначе false
                    bool isContinue = (unhandledCmd != null) && (unhandledCmd.Value.Item3 != null);

                    // Удаляем из списка connectedSockets listener-сокет lstrDataConnSock
                    if (!isContinue)
                        connectedSockets.Remove(lstrDataConnSock);

                    // Вспомогательный кортеж
                    (bool, string?)? tuple = null;

                    // Если это первая попытка получить файл
                    if (!isContinue)
                        tuple = isPathCorrect(arg, Cmd.RETR); // Тогда: проверяем корректность заданного пути к файлу

                    // Если путь указан некорректно
                    if (!isContinue && !tuple.Value.Item1)
                    {
                        isCtrlConnBlocked = false;

                        // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке
                        if (!SendResponse(Cmd.RETR, "550 Failed to open file.") && unhandledRes == null) return false;
                    }
                    else // Иначе
                    {
                        // Тогда:
                        // Получаем абсолютный путь к файлу
                        string absPath = isContinue ? unhandledCmd.Value.Item2 : tuple.Value.Item2;

                        // Создаём объект UnixFileInfo на основе пути absPath
                        UnixFileInfo file = new UnixFileInfo(absPath);

                        // Открываем поток для чтения файла
                        fstream = File.OpenRead(absPath);

                        // Размер буфера
                        int bufferSize = 1024 * 1024; // 1 MB

                        // Создаём массив байтов для считывания файла по частям
                        byte[] buffer = new byte[bufferSize];

                        // Вспомогательная переменная для количества считанных байт
                        int bytesRead = 0;

                        // Количество отправленных байт (за одну итерацию внутреннего цикла)
                        int currentBytesSend = 0;

                        try
                        {
                            // Если необходимо продолжить отправку данного файла
                            if (isContinue)
                            {
                                // Тогда:

                                // Изменяем позицию указателя в потоке на ту, на которой остановились в предыдущий раз
                                fstream.Seek(unhandledCmd.Value.Item3.Value, SeekOrigin.Begin);
                            }
                            else // Иначе, отправляем клиенту код 150
                            {
                                if (!SendResponse(Cmd.RETR, "150 Opening BINARY mode data connection for " + file.Name + " (" + file.Length.ToString() + " bytes).", true, arg)) return (unhandledRes == null) ? false : true;
                            }

                            // Считываем файл до тех пор, пока полностью не считаем
                            do
                            {
                                // Считываем байты файла
                                bytesRead = fstream.Read(buffer, 0, buffer.Length);

                                // Отправляем считанные байты файла клиенту
                                if (bytesRead > 0)
                                    currentBytesSend = cltDataConnSock.Send(buffer, 0, bytesRead, SocketFlags.None);
                                else
                                    currentBytesSend = 0;

                            } while ((bytesRead > 0) && (currentBytesSend == bytesRead));
                            // Цикл while работает до тех пора, пока:
                            // 1. файл полностью не будет отправлен
                            // 2. за последнюю итерацию цикла отправили столько байт, сколько и считали из файла

                            // Если отправили меньше байт, чем считали из файла
                            if (currentBytesSend < bytesRead)
                            {
                                // Тогда:
                                // => буфер сокета переполнен и необходимо продолжить отправку позже
                                // => необходимо подождать, пока клиент считает ранее отправленные байты

                                // Сохраняем в поле unhandledCmd необработанную команду, её аргумент (уже упрощённый путь к существующему файлу), а также позицию указателя, с которой необходимо будет продолжить передачу файла
                                unhandledCmd = (cmd, absPath, fstream.Position - (bytesRead - currentBytesSend));

                                // Если переполнение буфера при передаче данного файла случилось первый раз
                                if (!isContinue)
                                {
                                    // Тогда:

                                    // Блокируем для данного клиента обработку команд в управляющем соединении
                                    isCtrlConnBlocked = true;

                                    // Добавляем сокет cltDataConnSock в список connectedSockets, чтобы
                                    // отслеживать его состояние и когда данный сокет станет доступным для записи
                                    // вернуться и продолжить передачу файла
                                    connectedSockets.Add(new ConnectedSocket(cltDataConnSock, this, false, false, false, true));
                                }
                            }
                            else // Иначе, если отправка файла завершилась
                            {
                                // Отправляем клиенту код 226 - отправка файла успешно выполнена
                                if (!SendResponse(Cmd.RETR, "226 Transfer complete.", true)) return (unhandledRes == null) ? false : true;

                                // Закрываем соединение для передачи данных
                                CloseDataConnection(connectedSockets);
                            }
                        }
                        catch (Exception exc)
                        {
                            // => Завершаем передачу, так как произошла ошибка
                            // Отправляем клиенту код 550 - ошибка отправки файла - в управляющее соединение
                            if (!SendResponse(Cmd.RETR, "550 Failed to transfer file.", true)) return (unhandledRes == null) ? false : true;

                            // Закрываем соединение для передачи данных
                            CloseDataConnection(connectedSockets);
                        }
                        finally
                        {
                            // Освобождаем ресурсы, используемые fstream
                            fstream.Dispose();

                            fstream = null;
                        }
                    }
                } // Иначе, если необходимо выполнить загрузку файла на сервер
                else if (cmd == Cmd.STOR)
                {
                    // Тогда:

                    // Если необходимо продолжить получение данного файла, то isContinue = true, иначе false
                    bool isContinue = (unhandledCmd != null) && (unhandledCmd.Value.Item3 != null);

                    // Удаляем из списка connectedSockets listener-сокет lstrDataConnSock
                    if (!isContinue)
                        connectedSockets.Remove(lstrDataConnSock);

                    // Вспомогательный кортеж
                    (bool, string?)? tuple = null;

                    // Если это первая попытка получить файл
                    if (!isContinue)
                        tuple = isPathCorrect(arg, Cmd.STOR); // Тогда: проверяем корректность заданного пути к файлу

                    // Если путь указан некорректно
                    if (!isContinue && !tuple.Value.Item1)
                    {
                        // Тогда: отправляем клиенту отклик код 553 с сообщением об ошибке
                        if (!SendResponse(Cmd.STOR, "553 Could not create file.") && unhandledRes == null) return false;
                    }
                    else // Иначе
                    {
                        // Тогда:

                        // Получаем абсолютный путь к файлу
                        string absPath = isContinue ? unhandledCmd.Value.Item2 : tuple.Value.Item2;

                        // Открываем поток для записи файла
                        fstream = File.OpenWrite(absPath);

                        // Размер буфера
                        int bufferSize = 1024 * 1024; // 1 MB

                        // Создаём массив байтов для считывания файла по частям
                        byte[] buffer = new byte[bufferSize];

                        try
                        {

                            // Если необходимо продолжить получение данного файла
                            if (isContinue)
                                fstream.Seek(unhandledCmd.Value.Item3.Value, SeekOrigin.Begin); // Тогда: изменяем позицию указателя в потоке на ту, на которой остановились в предыдущий раз
                            else // Иначе, отправляем клиенту код 150
                            {
                                if (!SendResponse(Cmd.STOR, "150 Ok to send data.", true, arg)) return (unhandledRes == null) ? false : true;
                            }

                            int bytesRecv;

                            while (true)
                            {
                                // Если метод Poll вернул true
                                if (cltDataConnSock.Poll(0, SelectMode.SelectRead))
                                {
                                    // Тогда: возможны две причины по которым это произошло
                                    // 1. Поступление байтов в буфер сокета => .Available > 0
                                    // 2. Потеря связи с сокетом клиента => => .Available = 0

                                    // Если доступны байты для считывания
                                    if (cltDataConnSock.Available > 0)
                                    {
                                        // Тогда: считаем новые байты файла из буфера

                                        // Считываем байты файла из буфера
                                        bytesRecv = cltDataConnSock.Receive(buffer);

                                        // Записываем байты в файл
                                        fstream.Write(buffer, 0, bytesRecv);
                                    }
                                    else
                                    {
                                        // Тогда: сокет клиента для передачи данных отключился от сервера
                                        // => завершаем процесс получения файла

                                        // Отправляем клиенту код 226 - получение файла успешно выполнено - в управляющее соединение
                                        if (!SendResponse(Cmd.STOR, "226 Transfer complete.", true)) return (unhandledRes == null) ? false : true;

                                        // Выходим из цикла
                                        break;
                                    }
                                }
                                else
                                {
                                    // Тогда: необходимо отложить процесс получения данного файла
                                    // и вернуться к нему позже

                                    // Сохраняем в поле unhandledCmd необработанную команду, её аргумент (уже упрощённый путь к существующему файлу), а также позицию указателя, с которой необходимо будет продолжить записывать файл
                                    unhandledCmd = (cmd, absPath, fstream.Position);

                                    // Если это первая попытка получить данный файл
                                    if (!isContinue)
                                    {
                                        // Тогда:

                                        // Блокируем для данного клиента обработку команд в управляющем соединении
                                        isCtrlConnBlocked = true;

                                        // Добавляем сокет cltDataConnSock в список connectedSockets, чтобы
                                        // отслеживать его состояние и когда данный сокет станет доступным для чтения
                                        // вернуться и продолжить получение файла
                                        connectedSockets.Add(new ConnectedSocket(cltDataConnSock, this, false, false, false, true));
                                    }

                                    // Выходим из данного метода
                                    return true;
                                }
                            }
                        }
                        catch (Exception exc)
                        {
                            // Останавливаем процедуру получения файла на том моменте, на котором остановились

                            // Отправляем клиенту код 226 - получение файла успешно выполнено - в управляющее соединение
                            if (!SendResponse(Cmd.STOR, "226 Transfer complete.", true)) return (unhandledRes == null) ? false : true;
                        }
                        finally
                        {
                            // Освобождаем ресурсы, используемые fstream
                            fstream.Dispose();

                            fstream = null;
                        }

                        // Закрываем соединение для передачи данных
                        CloseDataConnection(connectedSockets);
                    }
                }
            }

            return true;
        }

        // Метод, который отправляет отклик сервера клиенту
        public bool SendResponse(Cmd? cmd, string? response, bool isNeedAddActs = false, string? arg = null)
        {
            // Массив байт, содержащий отклик сервера
            byte[] buffer = (unhandledRes == null) ? Encoding.ASCII.GetBytes(response + "\r\n") : unhandledRes.Value.Item2;

            // Общее количество отправленных байт
            int totalBytesSent = (unhandledRes == null) ? 0 : unhandledRes.Value.Item3;

            try
            {
                // Отправляем отклик сервера клиенту
                totalBytesSent += cltCtrlConnSock.Send(buffer, totalBytesSent, buffer.Length - totalBytesSent, SocketFlags.None);

                // Если отправилось меньше байт, чем требуется
                if (totalBytesSent < buffer.Length)
                {
                    // Тогда: => буфер сокета переполнен и необходимо продолжить отправку позже

                    // Сохраняем в поле unhandledRes ftp команду (в контексте которой происходит отправка отклика), неотправленный отклик и позицию, на которой остановилась отправка
                    unhandledRes = (cmd, buffer, totalBytesSent, isNeedAddActs, arg);

                    return false;
                }
            }
            catch (SocketException sockExc)
            {
                unhandledRes = null;

                // => Произошла ошибка
                return false;
            }

            unhandledRes = null;

            // Отправка отклика успешно закончилась
            return true;
        }

        // Метод, который обрабатывает команды клиента
        public bool HandleCmds(List<ConnectedSocket> connectedSockets)
        {
            // Буфер байтов для принятых команд от клиентов
            byte[] bytesRecv = null;

            // Количество считанных байт
            int numRecvBytes;

            // Текущая команда клиента
            string tmpCmd = string.Empty;

            // Буфер для хранения команды клиента
            bytesRecv = new byte[1024];

            // Записываем значение null в строку tmpCmd
            tmpCmd = null;

            try
            {
                // Запускаем цикл, который будет считывать все отправленные клиентом байты до тех пор,
                // пока не встретит строку, содержащую подряд идущие символы '\r\n'
                while (true)
                {
                    // Считываем команду
                    numRecvBytes = cltCtrlConnSock.Receive(bytesRecv);

                    // Выполняем декодировку полученных байт в строку типа string
                    tmpCmd += Encoding.ASCII.GetString(bytesRecv, 0, numRecvBytes);

                    // Если в очередной считанной строке содержатся подряд идущие символы '\r\n'
                    if (tmpCmd.Contains("\r\n"))
                        break; // Тогда: сервер полностью прочитал сообщение клиента => выходим из цикла
                }

                // Тайм аут в микросекундах, который метод Poll будет ожидать
                int timeout = 0; // 0 микросекунд

                // Если метод Poll вернул true и нет доступных команд для чтения
                if (cltCtrlConnSock.Poll(timeout, SelectMode.SelectRead) && (cltCtrlConnSock.Available == 0)) return false; // Тогда: данный клиент отключился от сервера => возвращаем: false

                // Производим предварительные действия с командой

                // Убираем из команды символы '\r' и '\n'
                tmpCmd = tmpCmd.Substring(0, tmpCmd.Length - 2);

                // Разбиваем команду по пробелам
                string[] cmdArr = tmpCmd.Split(' ');

                // Считываем имя команды и преобразуем в верхний регистр
                string cmd = cmdArr[0].ToUpperInvariant();

                // Проверяем какую имеено команду клиент запросил

                // Если клиент ещё не авторизовался
                if (!isAuth)
                {
                    // Тогда: клиенту доступны только 3 команды: USER, PASS, QUIT

                    // Если клиент запросил команду USER <username> - авторизацию
                    if (cmd == "USER")
                    {
                        isLogGet = true;

                        if (!SendResponse(Cmd.USER, "331 Please specify the password.") && unhandledRes == null) return false;
                    } // Иначе, если клиент запросил команду PASS <password> - ввод пароля
                    else if (cmd == "PASS")
                    {
                        // Если клиент не авторизован, но логин уже вводил
                        if (isLogGet)
                        {
                            // Тогда: подтверждаем успешную аунтентификацию клиента

                            // Меняем значение флага isAuth на true
                            isAuth = true;

                            // Высылаем код 530 с сообщением об успешной авторизации
                            if (!SendResponse(Cmd.PASS, "230 Login successful.") && unhandledRes == null) return false;
                        }
                        else // Иначе, если клиент ещё не вводил логин, а уже требует ввода пароля
                        {
                            // Тогда: высылаем клиенту код 503 и требуем его сначала авторизоваться

                            if (!SendResponse(Cmd.PASS, "503 Login with USER first.") && unhandledRes == null) return false;
                        }
                    } // Иначе, если клиент запросил команду QUIT - отключение от сервера
                    else if (cmd == "QUIT")
                    {
                        // Отправляем клиенту код: 221
                        SendResponse(Cmd.QUIT, "221 Goodbye.", true);

                        if (unhandledRes == null) return false; // Возвращаем: false, которое будет означать, что данного клиента необходимо удалить из списка всех клиентов
                    }
                    else // Иначе, если клиент ещё не авторизовался, а уже требует выполнение команд, не относящихся к авторизации и отключению от сервера
                    {
                        // Тогда: высылаем клиенту код 530 и требуем его авторизоваться

                        if (!SendResponse(null, "530 Please login with USER and PASS.") && unhandledRes == null) return false;
                    }
                } // Иначе, если клиент уже авторизовался
                else
                {
                    // Тогда: обрабатываем команды клиента

                    // Если клиент запросил команду USER <username> - авторизацию
                    if (cmd == "USER")
                    {
                        // Тогда: так как клиент уже авторизован под конкретным пользователем, то сообщаем клиенту о том, что сервер не может переключиться на другого пользователя
                        if (!SendResponse(Cmd.USER, "530 Can't change to another user.") && unhandledRes == null) return false;
                    }
                    else if (cmd == "PASS") // Иначе, если клиент запросил команду PASS <password> - ввод пароля
                    {
                        // Тогда: сообщаем клиенту о том, что он уже авторизован
                        if (!SendResponse(Cmd.PASS, "230 Already logged in.") && unhandledRes == null) return false;
                    }
                    else if (cmd == "SYST") // Иначе, если клиент запросил команду SYST - информация о системе, где запущен FTP Server
                    {
                        // Тогда: отправляем клиенту сведения о системе, на которой запущен FTP сервер
                        if (!SendResponse(Cmd.SYST, "215 UNIX Type: L8.") && unhandledRes == null) return false;
                    }
                    else if (cmd == "PWD") // Иначе, если клиент запросил команду PWD - текущая директория
                    {
                        // Тогда: отправляем клиенту текущую директорию
                        if (!SendResponse(Cmd.PWD, "257 \"" + currDir + "\" is the current directory.") && unhandledRes == null) return false;
                    }
                    else if (cmd == "CWD") // Иначе, если клиент запросил команду CWD <SP> <pathname> - изменить текущую директорию на <pathname>
                    {
                        // Тогда:

                        // Если в пути <pathname> содержится хотя бы одна директория с названием содержащим пробелы
                        // => разбиением по пробелам состоит > чем из 2-х символов
                        // => необходимо полностью считать подстроку после названия самой команды и <SP>
                        // иначе можно воспользоваться 1-м элементом массив cmdArr
                        string pathname = (cmdArr.Length > 2) ? tmpCmd.Substring(4) : cmdArr[1];

                        // Упрощаем путь <pathname>
                        string simplifiedPath = SimplifyPath(pathname, currDir);

                        // Если указанная директория <pathname> не существует
                        if (!Directory.Exists(((simplifiedPath == "/") ? workingDirPath : (workingDirPath + simplifiedPath)))) // Тогда: отправляем клиенту код 550 с сообщением об ошибке измении директории
                        {
                            if (!SendResponse(Cmd.CWD, "550 Failed to change directory.") && unhandledRes == null) return false;
                        }
                        else // Иначе, если данная директория существует
                        {
                            // Тогда: изменяем текущую директорию и отправляем клиенту код 250

                            // Изменяем путь к текущей директории
                            currDir = simplifiedPath;

                            // Отправляем клиенту отклик код 250 с сообщением об успешной смене директории
                            if (!SendResponse(Cmd.CWD, "250 Directory successfully changed.") && unhandledRes == null) return false;
                        }

                    } // Иначе, если клиент запросил команду MKD <SP> <pathname> - создать новую директорию
                    else if (cmd == "MKD")
                    {
                        // Тогда:

                        // Если в пути <pathname> содержится хотя бы одна директория с названием содержащим пробелы
                        // => разбиение по пробелам состоит > чем из 2-х символов
                        // => необходимо полностью считать подстроку после названия самой команды и <SP>
                        // иначе можно воспользоваться 1-м элементом массив cmdArr
                        string pathname = (cmdArr.Length > 2) ? tmpCmd.Substring(4) : cmdArr[1];

                        // Определяем новый путь к директории относительно корневого каталога '/'
                        string newDirPath = SimplifyPath(pathname, currDir);

                        // Определяем абсолютный путь к новой директории 'newDirPath'
                        string absNewDirPath = workingDirPath + newDirPath;

                        // Если клиент указал путь к директории, который после упрощения привёл к корневой директории
                        if (newDirPath == "/")
                        {
                            // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно создать данную директорию
                            if (!SendResponse(Cmd.MKD, "550 Create directory operation failed.") && unhandledRes == null) return false;
                        }
                        else // Иначе
                        {
                            // Тогда:

                            // Если указанная директория <pathname> уже существует
                            if (Directory.Exists(absNewDirPath))
                            {
                                // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно создать данную директорию
                                if (!SendResponse(Cmd.MKD, "550 Create directory operation failed.") && unhandledRes == null) return false;
                            }
                            else // Иначе, если такой директории ещё не существует
                            {
                                // Тогда:

                                // Пробуем создать данную директорию
                                try
                                {
                                    Directory.CreateDirectory(absNewDirPath);
                                    if (!SendResponse(Cmd.MKD, "257 \"" + newDirPath + "\" created.") && unhandledRes == null) return false;
                                } // Обрабатываем исключение
                                catch (Exception exc)
                                {
                                    // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно создать данную директорию
                                    if (!SendResponse(Cmd.MKD, "550 Create directory operation failed.") && unhandledRes == null) return false;
                                }
                            }
                        }

                    } // Иначе, если клиент запросил команду DELE <SP> <pathname> - удалить файл по указанному пути <pathname>
                    else if (cmd == "DELE")
                    {
                        // Тогда:

                        // Если в пути <pathname> к файлу содержится хотя бы одна директория с названием содержащим пробелы
                        // или название самого файла содержит пробелы
                        // => разбиением по пробелам состоит > чем из 2-х символов
                        // => необходимо полностью считать подстроку после названия самой команды и <SP>
                        // иначе можно воспользоваться 1-м элементом массив cmdArr
                        string pathname = (cmdArr.Length > 2) ? tmpCmd.Substring(5) : cmdArr[1];

                        // Если клиент пытается удалить корневую или домашнюю директории
                        if (pathname == "/" || pathname == "~")
                        {
                            // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно удалить данный файл
                            if (!SendResponse(Cmd.DELE, "550 Delete operation failed.") && unhandledRes == null) return false;
                        }
                        else // Иначе
                        {
                            // Тогда: необходимо проверить наличие файла в конце пути pathname

                            // Разбиваем путь к файлу по '/'
                            string[] splitPathName = pathname.Split('/');

                            // Если в конце пути содержится '/' вместо имени файла
                            if (splitPathName[splitPathName.Length - 1] == "")
                            {
                                // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно удалить данный файл
                                if (!SendResponse(Cmd.DELE, "550 Delete operation failed.") && unhandledRes == null) return false;
                            }
                            else // Иначе
                            {
                                // Тогда: необходимо упростить путь до файла и попытаться удалить указанный файл

                                // Упрощаем только путь к файлу
                                string simplifiedPath = SimplifyPath(pathname, currDir);

                                // Получаем абсолютный путь к файлу
                                string absFilePath = workingDirPath + simplifiedPath;

                                // Если указанный файл не существует
                                if (!File.Exists(absFilePath))
                                {
                                    // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно удалить данный файл
                                    if (!SendResponse(Cmd.DELE, "550 Delete operation failed.") && unhandledRes == null) return false;
                                }
                                else // Иначе
                                {
                                    // Тогда: пробуем удалить данный файл

                                    // Пробуем удалить указанный файл
                                    try
                                    {
                                        File.Delete(absFilePath);

                                        // Отправляем клиенту отклик код 250 с сообщением об успешном удалении указанного файла
                                        if (!SendResponse(Cmd.DELE, "250 Delete operation successful.") && unhandledRes == null) return false;
                                    } // Обрабатываем исключение
                                    catch (Exception exc)
                                    {
                                        // Тогда: отправляем клиенту отклик код 550 с сообщением об ошибке: невозможно удалить данный файл
                                        if (!SendResponse(Cmd.DELE, "550 Delete operation failed.") && unhandledRes == null) return false;
                                    }
                                }
                            }
                        }
                    } // Иначе, если клиент запросил команду PASV
                    else if (cmd == "PASV")
                    {
                        // Тогда: клиент хочет установить соединение для передачи данных в пассивном режиме
                        // таким образом, в этом случае серверу необходимо выделить ip адрес и порт для передачи данных для данного клиента

                        // Если пассивное соединение для передачи данных уже открыто
                        if (isPassiveOn)
                        {
                            // Тогда: необходимо его закрыть

                            // Удаляем из списка connectedSockets listener-сокет lstrDataConnSock
                            connectedSockets.Remove(lstrDataConnSock);

                            // Если lstrDataConnSock != null
                            if (lstrDataConnSock != null)
                            {
                                // Тогда: выключаем сокет

                                // Запрещаем операции Both - отправки и получения данных на сокете для прослушивания
                                lstrDataConnSock.Shutdown(SocketShutdown.Both);

                                // Закрываем сокет для прослушивания
                                lstrDataConnSock.Close();

                                // Записываем в данный сокет null
                                lstrDataConnSock = null;
                            }

                            // Если cltDataConnSock != null
                            if (cltDataConnSock != null)
                            {
                                // Запрещаем операции Both - отправки и получения данных на сокете клиента
                                cltDataConnSock.Shutdown(SocketShutdown.Both);

                                // Закрываем сокет клиента
                                cltDataConnSock.Close();

                                // Записываем в данный сокет null
                                cltDataConnSock = null;
                            }

                            // Сбрасываем последнюю необработанную команду
                            unhandledCmd = null;
                        }
                        else
                            isPassiveOn = true;

                        // Открываем новое соединение для передачи данных

                        // Ip и порт; 0 - означает, что когда будет осуществлять привязка сокета к порту,
                        // то операционная система сама выберет свободный порт
                        IPEndPoint localEndPoint = new IPEndPoint(serverIP, 0);

                        // Создаём потоковый сокет TCP/Ip
                        ConnectedSocket listener = new ConnectedSocket(serverIP.AddressFamily, SocketType.Stream, ProtocolType.Tcp, this, false, false, true);

                        // Переводим сокет в неблокирующий режим
                        listener.Blocking = false;

                        // Привязываем сокет к ip адресу и порту
                        listener.Bind(localEndPoint);

                        // Задаём максимальное количество клиентов в очереди на подключение
                        listener.Listen(1);

                        // Получаем объект, который содержит ip адрес и динамически найденный ОS порт
                        IPEndPoint ep = (IPEndPoint)listener.LocalEndPoint;

                        // Получаем динамический порт, который динамически выбрала OS
                        short port = (short)ep.Port;

                        // Представляем port в виде двух чисел: port = portArray[0] + portArray[1] * 256
                        byte[] portArray = BitConverter.GetBytes(port);

                        // Сохраняем listener-сокет для передачи данных для данного клиента
                        lstrDataConnSock = listener;

                        // Добавляем listener в список connectedSockets
                        connectedSockets.Add(listener);

                        // Отправляем клиенту код 227 с ip адресом и портом, на котором сервер открыл соединение для передачи данных
                        if (!SendResponse(Cmd.PASV, "227 Entering Passive Mode (127,0,0,1," + portArray[1].ToString() + "," + portArray[0].ToString() + ").") && unhandledRes == null) return false;
                    } // Иначе, если клиент запросил команду LIST [<SP> <pathname>] - листинг директории <pathname> или файла
                    else if (cmd == "LIST")
                    {
                        // Тогда:

                        // Если пассивное соединение для передачи данных уже открыто
                        if (isPassiveOn)
                        {
                            // Если в пути <pathname> содержится хотя бы одна директория с названием содержащим пробелы
                            // => разбиение по пробелам состоит > чем из 2-х символов
                            // => необходимо полностью считать подстроку после названия самой команды и <SP>
                            // иначе можно воспользоваться 1-м элементом массив cmdArr
                            string pathname = (cmdArr.Length > 2) ? tmpCmd.Substring(5) : ((cmdArr.Length > 1) ? cmdArr[1] : null);

                            // Обрабатываем данную команду, используя второе соединение
                            if (!HandleDataConnection(Cmd.LIST, pathname, connectedSockets)) return false;
                        }
                        else // Иначе, если соединение не открыто
                        {
                            // Тогда: требуем клиента открыть соединение для передачи данных
                            if (!RequireOpenDataConnection(Cmd.LIST) && unhandledRes == null) return false;
                        }
                    } // Иначе, если клиент запросил команду RETR <SP> <pathname> - скачивание файла с сервера
                    else if (cmd == "RETR")
                    {
                        // Тогда: необходимо отправить клиенту запрошенный файл

                        // Если пассивное соединение для передачи данных уже открыто
                        if (isPassiveOn)
                        {
                            // Если в пути <pathname> содержится хотя бы одна директория с названием содержащим пробелы
                            // => разбиение по пробелам состоит > чем из 2-х символов
                            // => необходимо полностью считать подстроку после названия самой команды и <SP>
                            // иначе можно воспользоваться 1-м элементом массив cmdArr
                            string pathname = (cmdArr.Length > 2) ? tmpCmd.Substring(5) : ((cmdArr.Length > 1) ? cmdArr[1] : null);

                            // Обрабатываем данную команду, используя второе соединение
                            if (!HandleDataConnection(Cmd.RETR, pathname, connectedSockets)) return false;
                        }
                        else // Иначе, если соединение не открыто
                        {
                            // Тогда: требуем клиента открыть соединение для передачи данных
                            if (!RequireOpenDataConnection(Cmd.LIST) && unhandledRes == null) return false;
                        }

                    } // Иначе, если клиент запросил команду STOR <SP> <pathname> - загрузка файла на сервер
                    else if (cmd == "STOR")
                    {
                        // Тогда: необходимо загрузить файл клиента на сервер

                        // Если пассивное соединение для передачи данных уже открыто
                        if (isPassiveOn)
                        {
                            // Если в пути <pathname> содержится хотя бы одна директория с названием содержащим пробелы
                            // => разбиение по пробелам состоит > чем из 2-х символов
                            // => необходимо полностью считать подстроку после названия самой команды и <SP>
                            // иначе можно воспользоваться 1-м элементом массив cmdArr
                            string pathname = (cmdArr.Length > 2) ? tmpCmd.Substring(5) : ((cmdArr.Length > 1) ? cmdArr[1] : null);

                            // Обрабатываем данную команду, используя второе соединение
                            if (!HandleDataConnection(Cmd.STOR, pathname, connectedSockets)) return false;
                        }
                        else // Иначе, если соединение не открыто
                        {
                            // Тогда: требуем клиента открыть соединение для передачи данных
                            if (!RequireOpenDataConnection(Cmd.LIST) && unhandledRes == null) return false;
                        }

                    }// Иначе, если клиент запросил команду TYPE <SP> <type-code> - тип передачи данных
                    else if (cmd == "TYPE")
                    {
                        // Тогда:

                        // Сервер работает только с бинарным (двоиным) режимом передачи данных
                        if (!SendResponse(Cmd.TYPE, "200 Switching to Binary mode.") && unhandledRes == null) return false;

                    }
                    else if (cmd == "QUIT") // Иначе, если клиент запросил команду QUIT - отключение от сервера
                    {
                        // Тогда:

                        // Отправляем клиенту код: 221
                        SendResponse(Cmd.QUIT, "221 Goodbye.", true);

                        if (unhandledRes == null)
                            return false; // Возвращаем: false, которое будет означать, что данного клиента необходимо удалить из списка всех клиентов
                    }
                    else // Иначе, если команда оказалась неизвестной/не реализованной
                    {
                        if (!SendResponse(null, "502 Command not implemented.") && unhandledRes == null) return false;
                    }
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine("Error: Exception: " + exc);
            }

            // Возвращаем true
            return true;
        }
    }
}