using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

namespace ftpserver
{
    // Класс, который реализует FTP-сервер
    public class Server
    {
        // Поля:

        // Ip адрес, на котором запущен сервер
        IPAddress ip;

        // Порт, на котором запущен сервер
        int port;

        // Путь к рабочей директории
        string workingDirPath;

        // Список, содержащий подключённые когда-либо клиентские сокеты
        List<ConnectedSocket> connectedSockets;

        // Конструктор с входными параметрами: ip и port сервера
        public Server(string _ip, int _port, string _workingDirPath)
        {
            ip = IPAddress.Parse(_ip);
            port = _port;
            workingDirPath = _workingDirPath;
            connectedSockets = new List<ConnectedSocket>();
        }

        // Метод, который принимает новых клиентов
        void AcceptClients(ConnectedSocket listener)
        {
            // Тайм аут в микросекундах, который метод Poll будет ожидать
            int timeout = 0; // 0 микросекунд

            // Принимаем всех клиентов, ожидающих подключения
            while (listener.Poll(timeout, SelectMode.SelectRead))
            {
                // Принимаем очередного клиента
                Socket currentClientSock = listener.Accept();

                // Однако не спешим его добавлять в список клиентов, так как возможно, что он уже отключился
                // поэтому необходимо проверить активен ли данный клиент

                // Если данный клиент всё еще подключён к серверу
                if (isClientConnected(currentClientSock))
                {
                    // Тогда:

                    // Создаём на основе текущего клиентского сокета сокет типа ConnectedSocket,
                    // который помимо самого сокета currentClientSock ещё будет содержать
                    // объект клиента типа Client
                    ConnectedSocket tmpConnectedSock = new ConnectedSocket(currentClientSock, new Client(currentClientSock, null, ip, workingDirPath), false, true);

                    // Добавляем сокет данного клиента в список всех подключённых ранее клиентов
                    connectedSockets.Add(tmpConnectedSock);

                    // Отправляем клиенту код приветствия: 220
                    currentClientSock.Send(Encoding.ASCII.GetBytes("220 (MyFTP 1.0)\r\n"));
                }
                else // Иначе, если клиент уже отключился
                    continue; // => переходим к следующему клиенту
            }
        }

        // Метод, который удаляет клиента client из списка connectedSockets
        void RemoveClient(ConnectedSocket ConnectedClientSock)
        {
            // Получаем объект клиента, который соответствует данному клиентскому сокету ConnectedClientSock
            Client currentClient = ConnectedClientSock.Client;

            // Закрываем сокет для работы с управляющим соединением
            if (currentClient.CltCtrlConnSock != null)
                currentClient.CltCtrlConnSock.Close();

            // Закрываем сокет для работы с соедининем для передачи данных (client)
            if (currentClient.CltDataConnSock != null)
                currentClient.CltDataConnSock.Close();

            // Закрываем сокет для работы с соедининем для передачи данных (listener)
            if (currentClient.LstrDataConnSock != null)
                currentClient.LstrDataConnSock.Close();

            // Получаем id клиента
            long clientId = currentClient.GetId;

            // Вспомогательная переменная
            ConnectedSocket sock = null;

            // Удаляем из списка connectedSockets все сокеты клиента с данным clientId
            for (int i = 0; i < connectedSockets.Count; i++)
            {
                sock = connectedSockets[i];

                if (sock.Client.GetId == clientId)
                    connectedSockets.RemoveAt(i--);
            }
        }

        // Метод, который обрабатывает команды клиента или определяет, что данный клиент отключился от сервера
        // и удаляет его из соответствующих списков
        void HandleClientCmds(ConnectedSocket ConnectedClientSock)
        {
            // Получаем объект клиента, который соответствует данному клиентскому сокету ConnectedClientSock
            Client currentClient = ConnectedClientSock.Client;


            // Тогда: обрабатываем команду данного клиента

            // Возможны 3 ситуации
            // 1. Данный клиент отправил команду серверу и на момент вызова метода Select() был подключён к серверу
            // 2. Данный клиент не отправлял никаких команд, а просто отключился от сервера
            // 3. Данный клиент отправил команду серверу и на момент вызова метода Select() уже был отключён от сервера

            // Если клиент не отправлял никаких команд
            if (currentClient.CltCtrlConnSock.Available == 0) // Тогда: это означает, что клиент просто отключился от сервера => необходимо удалить данного клиента из списка клиентов
                RemoveClient(ConnectedClientSock); // Удаляем данного клиента из списка всех подключённых клиентов connectedSockets
            else // Иначе, если клиент отправил команду
            {
                // Тогда: возможны 2 ситуации:

                // 1. Данный клиент отправил команду серверу и на момент вызова метода Select() был подключён к серверу
                // 2. Данный клиент отправил команду серверу и на момент вызова метода Select() уже был отключён от сервера

                // Вызываем метод для обработки команд
                // Если метод вернёт false
                if (!currentClient.HandleCmds(connectedSockets)) // Тогда: это означает, что клиент отключился от сервера => удалим его из списка клиентов
                    RemoveClient(ConnectedClientSock); // Удаляем данного клиента из списка всех подключённых клиентов connectedSockets
            }
        }

        // Метод, который обрабатывает команды, связанные с соединением для передачи данных
        void HandleDataConnectionCmds(ConnectedSocket sock)
        {
            // Получаем объект клиента, который соответствует данному клиентскому сокету sock (для передачи данных)
            Client currentClient = sock.Client;

            // Если клиент ещё не подключился к соединению для передачи данных
            if (currentClient.CltDataConnSock == null)
            {
                // Тогда: обрабатываем данного клиента

                // Получаем сокет клиента для работы с соединением для передачи данных и сохраняем его в объекте клиента
                currentClient.CltDataConnSock = sock.Accept();


                // Если клиент подключился к управлящему соединению после того, как запросил команду, требующую данное соединение
                if (currentClient.UnhandledCmd != null)
                {
                    // Тогда: необходимо вручную вызвать данную необработанную команду
                    // => клиент подключился к соединению для передачи данных после вызова команды

                    if (!currentClient.HandleDataConnection(currentClient.UnhandledCmd.Value.Item1, currentClient.UnhandledCmd.Value.Item2, connectedSockets)) RemoveClient(sock);
                }
                else // Иначе, если команда, требующая соединения для передачи данных ещё не была вызвана => клиент подключился к соединению для передачи данных раньше времени
                    connectedSockets.Remove(sock); // Тогда: можно удалить данный listener-сокет из списка, так как клиент уже подключился, а подключать других клиентов - не нужно

            }
            else // Иначе, если клиент уже подключился к соединению для передачи данных
            {
                // Тогда:

                // Получаем необработанную команду клиента
                (Client.Cmd, string, long?) unhandledCmd = currentClient.UnhandledCmd.Value;

                // Вручную вызываем метод HandleDataConnection с командой RETR / STOR / LIST
                if (!currentClient.HandleDataConnection(unhandledCmd.Item1, unhandledCmd.Item2, connectedSockets)) RemoveClient(sock);
            }
        }

        // Метод, который обрабатывает ранее неотправленные полностью отклики сервера
        void HandleServerResponse(ConnectedSocket sock)
        {
            (Client.Cmd?, byte[], int, bool, string?)? unhandledResp = sock.Client.RecentUnhandledResponse;
            Client client = sock.Client;

            if (unhandledResp.Value.Item4)
            {
                if (unhandledResp.Value.Item1 == Client.Cmd.QUIT)
                {
                    client.SendResponse(Client.Cmd.QUIT, null, true);

                    if (client.RecentUnhandledResponse == null) RemoveClient(sock);
                }
                else // Иначе, если необходимо обработать команды: RETR, STOR, LIST
                {
                    // Если необходимо обработать отклики 226 и 550 (true)
                    if (unhandledResp.Value.Item5 == null)
                    {
                        if (!client.SendResponse(unhandledResp.Value.Item1, null, true) && client.RecentUnhandledResponse == null)
                            RemoveClient(sock);
                        else if (client.RecentUnhandledResponse == null) // Иначе, если client.SendResponse(Client.Cmd.STOR или Client.Cmd.RETR или Client.Cmd.LIST, null, true) вернул true
                        {
                            // Закрываем соединение для передачи данных
                            client.CloseDataConnection(connectedSockets);
                        }
                    }
                    else // => необходимо обработать отклик 150
                        if (!client.HandleDataConnection(unhandledResp.Value.Item1.Value, unhandledResp.Value.Item5, connectedSockets)) RemoveClient(sock);
                }
            }
            else
            {
                if (!client.SendResponse(unhandledResp.Value.Item1, null) && client.RecentUnhandledResponse == null) RemoveClient(sock);
            }
        }

        // Метод, который проверяет является ли сокет клиента подключенным к серверу
        public bool isClientConnected(Socket client)
        {
            bool flag1 = client.Poll(0, SelectMode.SelectRead);

            bool flag2 = (client.Available == 0);

            if ((flag1 && flag2) || !client.Connected)
                return false;
            else
                return true;
        }

        // Метод, который запускает сервер
        public void Execute()
        {
            // Создаём локальную конечную точку, на которой будет запущен FTP сервер
            IPEndPoint localEP = new IPEndPoint(ip, port);

            // Создаём потоковый сокет TCP
            ConnectedSocket listener = new ConnectedSocket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // Переводим сокет в неблокирующий режим
            listener.Blocking = false;

            // Привязываем сокет к конечной локальной точке
            listener.Bind(localEP);

            // Включаем прослушивание на сокете
            listener.Listen();

            // Создаём вспомогательный список для чтения новых клиентских сокетов
            List<ConnectedSocket> readableSockets = new List<ConnectedSocket>();

            List<ConnectedSocket> writableSockets = new List<ConnectedSocket>();

            try
            {
                // Обрабатываем каждого клиента и его запросы
                while (true)
                {
                    // Очищаем вспомогательный список readableSockets
                    readableSockets.Clear();

                    // Очищаем вспомогательный список writableSockets
                    writableSockets.Clear();

                    // Добавляем в список readableSockets сокет listener
                    readableSockets.Add(listener);
                    
                    // Копируем из списка connectedSockets в списки readableSockets и writableSockets полученные ранее сокеты
                    foreach (ConnectedSocket sock in connectedSockets)
                    {
                        if ((sock.IsCltDataConnSock && sock.Client.RecentUnhandledCmd == Client.Cmd.RETR) || (sock.IsCltControlConnectionSock && sock.Client.RecentUnhandledResponse != null))
                            writableSockets.Add(sock);
                        else if (sock.Client == null || !sock.IsCltControlConnectionSock || !sock.Client.IsCtrlConnBlocked)
                            readableSockets.Add(sock);
                    }

                    // Ожидаем, пока какой-то из сокетов не совершит какое-либо событие:
                    // 1. если к серверу подключатся новые клиенты, то метод Select()
                    // передаст управление программе и сокет listener останется в списке readableSockets
                    // 2. если кто-то из уже подключенных клиентов отправит серверу запрос, то
                    // метод Select() передаст управление программе и
                    // соответствующие данным клиентам сокеты останутся в списке readableSockets
                    // 3. Если клиент подключится к соединению для передачи данных, то метод Select()
                    // передаст управление программе и соответствующий сокет останется в списке readableSockets
                    // 4. Если сокет для передачи данных будет доступен для чтения/записи, то метод Select()
                    // передаст управление программе и соответствующий сокет останется в списке readableSockets/writableSockets
                    ConnectedSocket.Select(readableSockets, writableSockets, null, -1);

                    // Обрабатываем каждый сокет, который остался в списке readableSockets после вызова метода Select()
                    foreach (ConnectedSocket sock in readableSockets)
                    {
                        // Если данный сокет является listener сокетом
                        if (sock.IsLstrServerConnSock) // Тогда: необходимо принять все новые подключения, добавить их в список connectedSockets и создать новых клиентов, добавив их в список clients
                            AcceptClients(sock); // Обрабатываем новых клиентов
                        else if (sock.IsCltControlConnectionSock) // Иначе, если данный сокет является control connection сокетом
                        {
                            // Тогда: необходимо обработать команды данного клиента
                            // или же если клиент отключился от сервера, удалить его из списка
                            // connectedSockets

                            // Обрабатываем команды клиента
                            HandleClientCmds(sock);
                        }
                        else // Иначе, если данный сокет является client/listener data connection сокетом
                        {
                            // Тогда:

                            // Обрабатываем данный сокет
                            HandleDataConnectionCmds(sock);
                        }
                    }

                    // Обрабатываем каждый сокет, который остался в списке writableSockets после вызова метода Select()
                    foreach (ConnectedSocket sock in writableSockets)
                    {
                        // Если данный сокет является client data connection сокетом
                        if (sock.IsCltDataConnSock)
                        {
                            // Тогда:

                            // Обрабатываем данный сокет
                            HandleDataConnectionCmds(sock);
                        }
                        else // Иначе, если данный сокет является client control connection сокетом
                        {
                            // Тогда:

                            // Обрабатываем данный сокет
                            HandleServerResponse(sock);
                        }
                    }
                }
            }
            catch (SocketException sockExc)
            {
                Console.WriteLine("Error: Socket exception: " + sockExc.SocketErrorCode);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Error: Exception: " + exc);
            }
            finally
            {
                // Закрываем все оставшиеся сокеты клиентов
                foreach (ConnectedSocket client in connectedSockets)
                {
                    if (client.Client.CltCtrlConnSock != null)
                        client.Client.CltCtrlConnSock.Close(); // Закрываем сокет

                    if (client.Client.LstrDataConnSock != null)
                        client.Client.LstrDataConnSock.Close(); // Закрываем сокет
                }

                // Очищаем список connectedSockets
                connectedSockets.Clear();
            }
        }
    }
}