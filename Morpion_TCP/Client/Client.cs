﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyClient.Functions;
using MyClient.Models;
using MyClient.ModelGame;
using System.Numerics;

namespace MyClient
{
    public class MyClient
    {

        public readonly string LogFile;
        public Int32 Port = 13000;
        public IPAddress Ip = IPAddress.Parse("127.0.0.1");//127.0.0.1

        private Socket socket = null;
        private IPEndPoint remoteEP = null;
        private bool continueListening = false;
        private Thread listeningThread = null;
        private Thread pingThread = null;

        private readonly object streamLock;
        public NetworkStream Stream = null;

        private readonly object usersLock = new object();
        private Dictionary<int, User> connectedUsers = new Dictionary<int, User>();
        public Dictionary<int, User> ConnectedUsers
        {
            set
            {
                lock (usersLock)
                {
                    connectedUsers = value;
                }
            }
            get
            {
                Dictionary<int, User> connectedUsers_copy;
                lock (usersLock)
                {
                    connectedUsers_copy = new Dictionary<int, User>(connectedUsers);
                }
                return connectedUsers_copy;

            }
        }


        public Dictionary<int, User> gameRequestsRecieved = new Dictionary<int, User>();
        public User Opponent = null;
        public Game GameClient = null;

        public readonly LogWriter LogWriter;

        private static Dictionary<NomCommande, Action<byte[], MyClient>> methods = new Dictionary<NomCommande, Action<byte[], MyClient>>();

        public static void InnitMethods()
        {
            methods[NomCommande.OUS] = Messaging.RecieveOtherUsers;
            methods[NomCommande.RGR] = Messaging.RecieveGameRequestStatus;
            methods[NomCommande.MRQ] = Messaging.RecieveGameRequest;
            methods[NomCommande.DGB] = Messaging.RecieveGameBoard;
            methods[NomCommande.MSG] = Messaging.RecieveMessage;
            methods[NomCommande.PNG] = Messaging.RecievePing;
        }

        public MyClient()
        {
            string to_date_string = DateTime.Now.ToString("s");
            Directory.CreateDirectory("logs");
            LogFile = "logs/client_log_" + to_date_string + ".txt";
            LogFile = LogFile.Replace(':', '_');

            LogWriter = new LogWriter(LogFile);
            streamLock = new object();
        }

        public void tryConnect()
        {
            if( this.socket == null || !this.socket.Connected)
            {
                this.remoteEP = new IPEndPoint(this.Ip, this.Port);
                this.socket = new Socket(this.Ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                //connection to the server
                this.socket.Connect(this.remoteEP);
                if (this.socket.Connected)
                {
                    this.continueListening = true;

                    this.Stream = new NetworkStream(this.socket);
                    this.Stream.ReadTimeout = 10;

                    //launching the listening thread
                    this.listeningThread = new Thread(Listen);
                    this.listeningThread.Start();

                    //launching the ping thread
                    this.pingThread = new Thread(Ping);
                    this.pingThread.Start();
                }
            }
            
            
        }

        public void tryDisconnect()
        {
            if (this.socket != null && this.socket.Connected)
            {
                this.continueListening = false;
                this.socket.Close();
                Console.WriteLine("Déconnection effectuée");
            }
        }

        void Ping()
        {
            while (this.continueListening)
            {
                try
                {
                    Messaging.SendPing(this);
                }
                catch (Exception) //à faire: prendre en compte la fermeture innatendue du canal par le serveur
                {
                    this.continueListening = false;
                    tryDisconnect();
                }
                Thread.Sleep(1000);
            }
        }

        void Listen()
        {
            while (this.continueListening)
            {

                try
                {
                    Byte[] bytes = new Byte[5];
                    int n_bytes = 0;
                    try
                    {
                        n_bytes = StreamRead(bytes);
                    }
                    catch (IOException ex)
                    {
                        n_bytes = 0;
                    }
                    
                    if (n_bytes >= 5) //minimum number of bytes for CMD + length to follow
                    {

                        string cmd = System.Text.Encoding.UTF8.GetString(bytes, 0, 3);
                        int following_length = BitConverter.ToInt16(bytes, 3);

                        byte[] following_bytes = new byte[following_length];
                        if (following_length > 0)
                        {
                            StreamRead(following_bytes);
                        }
                        if(cmd != "PNG")
                        {
                            Console.WriteLine($" >> command recieved from the serveur : {cmd} de taille {following_length} {n_bytes}");
                        }
                        
                        try
                        {
                            NomCommande cmd_type = (NomCommande)Enum.Parse(typeof(NomCommande), cmd);
                            if(cmd != "PNG")
                            {
                                LogWriter.Write($"command recieved: {cmd}, following_length: {following_length}");
                            }
                            MyClient.methods[cmd_type](following_bytes, this);
                            
                        }
                        catch(Exception ex)
                        {
                            //write_in_log
                            LogWriter.Write($"CMD ERROR, CMD: {cmd}, following_length: {following_length}, EX:{ex}");
                            this.Stream.Flush();
                        }
                        
                    }

                    Thread.Sleep(10);

                }
                catch (Exception ex) //à faire: prendre en compte la fermeture innatendue du canal par le serveur
                {
                    this.continueListening = false;
                    LogWriter.Write($"ERROR: Listen crashed:  {ex}");
                    Console.WriteLine(" >> " + ex.ToString());
                }
            }
        }

        void DisplayOtherUser()
        {
            Console.WriteLine($"Voici les {ConnectedUsers.Count} autres utilisateurs:");
            foreach (var user in ConnectedUsers.Values)
            {
                user.Display();
            }
        }

        void DisplayMatchRequest()
        {
            Console.WriteLine($"Voici les {gameRequestsRecieved.Count} requêtes reçues");
            foreach (var r in gameRequestsRecieved.Values)
            {
                r.Display();
            }
        }

        void DisplayGameBoard()
        {
            Console.WriteLine($">>Voici le plateau du jeu");
            GameBoard.display(this.GameClient.GameBoardMatrix); //ajouter une exception
            if ((this.GameClient.Mode == GameMode.Player1 && this.GameClient.IdPlayer1 != this.Opponent.Id)|| (this.GameClient.Mode == GameMode.Player2 && this.GameClient.IdPlayer2 != this.Opponent.Id))
            {
                Console.WriteLine(">> C'est a votre tour de jouer");
            }
            else if ((this.GameClient.Mode == GameMode.Player1Won && this.GameClient.IdPlayer1 != this.Opponent.Id) || (this.GameClient.Mode == GameMode.Player2Won && this.GameClient.IdPlayer2 != this.Opponent.Id))
            {
                Console.WriteLine(">> Vous avez gagné");
            }
            else if ((this.GameClient.Mode == GameMode.Player1Won) || (this.GameClient.Mode == GameMode.Player2Won ))
            {
                Console.WriteLine(">> Vous avez perdu");
            }
            else
            {
                Console.WriteLine(">> Ce n'est pas a votre tour de jouer");
            }
        }

        public int StreamRead(byte[] message)
        {
            int n_bytes = 0;
            lock (streamLock)
            {
                n_bytes = this.Stream.Read(message, 0, message.Length);
            }
            return n_bytes;
        }

        public void StreamWrite(byte[] message)
        {
            lock (streamLock)
            {
                this.Stream.Write(message, 0, message.Length);
            }
        }

        static void Main(string[] args)
        {
            MyClient my_client = new MyClient();
            MyClient.InnitMethods();
            Console.WriteLine("Bonjour Client !");

            //entering the commands loop
            bool continuer = true;
            while (continuer)
            {
                try
                {
                    Console.WriteLine("Que voulez-vous faire?" +
                    "\n\t0-envoyer un message" +
                    "\n\t1-demander les utilisateurs connectés" +
                    "\n\t2-changer de UserName" +
                    "\n\t3-afficher les utilisateurs connectés" +
                    "\n\t4-afficher les rêquetes de match" +
                    "\n\t5-répondre à une requête de match" +
                    "\n\t6-exprimer une requête de match" +
                    "\n\t7-se déconnecter" +
                    "\n\t8-se connecter" +
                    "\n\t9-afficher l'id de l'adversaire" +
                    "\n\t10-actualiser le plateau" +
                    "\n\t11-afficher le plateau" +
                    "\n\t12-Jouer une position");
                    string choice = Console.ReadLine();
                    if (choice == "0")
                    {

                        Console.WriteLine("entrez un message");
                        string message = Console.ReadLine();
                        Messaging.SendMessage(my_client, message);

                    }
                    else if (choice == "1")
                    {
                        Messaging.AskOtherUsers(my_client);
                        Console.WriteLine("La demande a été émise");
                    }
                    else if (choice == "2")
                    {
                        Console.WriteLine("entrez un nom d'utilisateur");
                        string userName = Console.ReadLine();
                        Messaging.SendUserName(my_client, userName);
                    }
                    else if (choice == "3")
                    {
                        my_client.DisplayOtherUser();
                    }
                    else if (choice == "4")
                    {
                        my_client.DisplayMatchRequest();
                    }
                    else if (choice == "5")
                    {
                        Console.WriteLine("entrez l'id de l'adversaire:");
                        int id = Convert.ToInt32(Console.ReadLine());
                        Console.WriteLine("entrez votre réponse:");
                        bool accepted = Convert.ToBoolean(Console.ReadLine());
                        Messaging.SendGameRequestResponse(my_client, id, accepted);
                    }
                    else if (choice == "6")
                    {
                        Console.WriteLine("Entrez l'id de l'adversaire souhaité:");
                        int id = Convert.ToInt32(Console.ReadLine());
                        Messaging.RequestMatch(my_client, id);
                        Console.WriteLine("Requête envoyée");
                    }
                    else if (choice == "7")
                    {
                        my_client.tryDisconnect();

                    }
                    else if (choice == "8")
                    {
                        my_client.tryConnect();
                        Console.WriteLine($"Connected to the server {my_client.Ip}");
                    }
                    else if (choice == "9")
                    {
                        if (my_client.Opponent != null)
                        {
                            Console.WriteLine($"L'id de votre adversaire est: {my_client.Opponent.Id} et son user name est: {my_client.Opponent.UserName}");
                        }
                        else
                        {
                            Console.WriteLine($"Aucun adversaire n'est attribué");
                        }
                    }
                    else if (choice == "10")
                    {
                        Messaging.AskGameBoard(my_client);
                        Console.WriteLine($"Requête envoyée");
                    }
                    else if (choice == "11")
                    {
                        my_client.DisplayGameBoard();
                    }
                    else if (choice == "12")
                    {
                        Console.WriteLine($"my_client.GameClient.Mode : {my_client.GameClient.Mode }; GameMode.Player1: {GameMode.Player1}; GameMode.Player2 : {GameMode.Player2}");
                        Console.WriteLine($"my_client.GameClient.IdPlayer1 : {my_client.GameClient.IdPlayer1 }; my_client.GameClient.IdPlayer2: {my_client.GameClient.IdPlayer2}; my_client.Opponent.Id : {my_client.Opponent.Id}");
                        {
                            Vector3 position = new Vector3();
                            int x = 0;
                            int y = 0;
                            int z = 0;
                            Console.WriteLine("Quelle est la coordonnee x (0,1 ou 2) de la position que vous voulez jouer ? (La couche)");
                            x = (int.Parse(Console.ReadLine()));
                            position.X = x;
                            Console.WriteLine("Quelle est la coordonnee y (0,1 ou 2) de la position que vous voulez jouer ? (la ligne) ");
                            y = (int.Parse(Console.ReadLine()));
                            position.Y = y;
                            Console.WriteLine("Quelle est la coordonnee z (0,1 ou 2) de la position que vous voulez jouer ? (la colonne)");
                            z = (int.Parse(Console.ReadLine()));
                            position.Z = z;
                            Messaging.SendPositionPlayer(my_client, position);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Commande inconnue");
                    }
                }
                catch (System.IO.IOException ex)
                {
                    Console.WriteLine("You have been disconnected from the server, please connect again");
                    my_client.tryDisconnect();
                }
                

            }

        }

        
    }
}
