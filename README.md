# FTP Server
This is a simple implementation of FTP server in C# + NET 5.0 for Linux Ubuntu 20.04 as a class project for operating systems at [Russian Technological University (MIREA)](https://www.mirea.ru/) in fall semester 2020.


## The main features of this implementation
- Developed and tested under Linux Ubuntu 20.04. However, you can try running it on other operating systems as well (but some changes are needed).
- Console application that requires features from NET 5.0.
- Works with FileZilla and standard Linux terminal tools: ftp and telnet.
- Uses a single thread with non-blocking sockets. So, it can work simultaneously with several clients.
- Developed according to [RFC 959](https://tools.ietf.org/html/rfc959).
- Doesn't have complete authentication. You can enter any username and password.
- The same directory is used for all users.
- Supports only the following type of data transfer:
	1. Mode: stream
	2. Type: binary/image
	3. Form: non-print
	4. Structure: file
- Supports only passive data (PASV) transfer mode.
- Supports the minimum required ftp commands.

## General architecture
This implementation consists of three classes: ```Server```, ```Client```, and ```ConnectedSocket```. The ```Server``` class contains the main ```Execute()``` method. This method uses the static method ``` Socket.Select()```, which sleeps indefinitely until one of the sockets performs an action.
The ```ConnectedSocket``` class is a wrapper over class ```System.Net.Sockets.Socket``` (```ConnectedSocket``` class inherits from the ```System.Net.Sockets.Socket``` class) and adds additional information necessary for working with the client.
The ```Client``` class contains all the FTP connection state for a specific client.

## How to use
Clone this repository and build the solution. You can use the ```dotnet run [arg1] [arg2] [arg3]``` command to run it. You can pass optional arguments \[arg1\], \[arg2\], \[arg3\]:
- arg1 - ip address on which you want to run the server. Default: ```127.0.0.1```.
- arg2 - port on which you want to run the server. Default: ```11000```.
- arg3 - working directory.

![Example 1](https://github.com/archie1602/FTPServer/blob/master/img/Example1.png)

![Example 2](https://github.com/archie1602/FTPServer/blob/master/img/Example2.png)

## Supported FTP commands
- USER
- PASS
- SYST
- PWD
- CWD
- MKD
- DELE
- PASV
- LIST
- RETR
- STOR
- TYPE
- QUIT

## Resources used
- [RFC 959](https://tools.ietf.org/html/rfc959)
- [List of FTP commands](https://en.wikipedia.org/wiki/List_of_FTP_commands)
- [telnet Linux](https://linux.die.net/man/1/telnet)
- [MSDN C# Sockets](https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket?view=net-5.0)
