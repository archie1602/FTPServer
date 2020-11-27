using System;
using System.Net;
using System.Net.Sockets;

namespace ftpserver
{
    // Класс, который оборачивает класс Socket и добавляет к нему дополнительные данные для работы с ftp протоколом
    public class ConnectedSocket : Socket
    {
        // Поля

        // Данный сокет представляет один из следующих 4 сокетов ftp протокола:

        // Bool flag - is listener server connection socket
        bool isLstrServerConnSock;

        // Bool flag - is client control connection socket
        bool isCltControlConnectionSock;

        // Bool flag - is listener data connection socket
        bool isLstrDataConnSock;

        // Bool flag - is client data connection socket
        bool isCltDataConnSock;

        // Объект клиента, которому данный сокет принадлежит
        Client client;

        // Конструкторы класса

        public ConnectedSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, Client _client = null, bool _isLstrServerConnSock = true, bool _isCltControlConnectionSock = false, bool _isLstrDataConnSock = false, bool _isCltDataConnSock = false) : base(addressFamily, socketType, protocolType)
        {
            isLstrServerConnSock = _isLstrServerConnSock;
            isCltControlConnectionSock = _isCltControlConnectionSock;
            isLstrDataConnSock = _isLstrDataConnSock;
            isCltDataConnSock = _isCltDataConnSock;
            client = _client;
        }

        public ConnectedSocket(Socket sock, Client _client, bool _isLstrServerConnSock = false, bool _isCltControlConnectionSock = false, bool _isLstrDataConnSock = false, bool _isCltDataConnSock = false) : base(sock.SafeHandle)
        {
            base.Blocking = sock.Blocking;
            isLstrServerConnSock = _isLstrServerConnSock;
            isCltControlConnectionSock = _isCltControlConnectionSock;
            isLstrDataConnSock = _isLstrDataConnSock;
            isCltDataConnSock = _isCltDataConnSock;
            client = _client;
        }

        // Get и Set свойства
        public bool IsLstrServerConnSock
        {
            get { return isLstrServerConnSock; }
            set { isLstrServerConnSock = value; }
        }

        public bool IsCltControlConnectionSock
        {
            get { return isCltControlConnectionSock; }
            set { isCltControlConnectionSock = value; }
        }

        public bool IsLstrDataConnSock
        {
            get { return isLstrDataConnSock; }
            set { isLstrDataConnSock = value; }
        }

        public bool IsCltDataConnSock
        {
            get { return isCltDataConnSock; }
            set { isCltDataConnSock = value; }
        }

        public Client Client
        {
            get { return client; }
            set { client = value; }
        }
    }
}