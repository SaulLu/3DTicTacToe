﻿using Serveur.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Serveur.ModelGame;

namespace Serveur.Functions
{
    public enum NomCommande
    {
        MSG,
        USN,
        OUS,
        NPP,
        DGB
    }

    public class Messaging
    {
        private static byte[] serializationMessage(byte[] message_bytes, NomCommande nomCommande)
        {
            //command in bytes
            var cmd = Encoding.UTF8.GetBytes(nomCommande.ToString());
            //length of the content in bytes
            var message_length = BitConverter.GetBytes((Int16)message_bytes.Length);

            byte[] msg = new byte[cmd.Length + message_length.Length + message_bytes.Length];

            //command
            cmd.CopyTo(msg, 0);
            //length to follow
            message_length.CopyTo(msg, cmd.Length);
            //content
            message_bytes.CopyTo(msg, cmd.Length + message_length.Length);

            //renvoie le tableau de bytes
            return msg;
        }

        public static byte[] RecieveUserName(byte[] bytes, UserHandler userHandler)
        {
            string userName = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            userHandler.UserName = userName;
            Console.WriteLine($" >> message recieved from client Id {userHandler.Id} its new userName: {userHandler.UserName}");
            return new byte[0];
        }

        public static byte[] RecieveMessage(byte[] bytes, UserHandler userHandler)
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            Console.WriteLine($" >> message recieved from client {userHandler.UserName} Id {userHandler.Id} : {message}");
            return new byte[0];
        }

        public static byte[] SendOtherUsers(byte[] bytes, UserHandler userHandler)
        {
            
            var bytes_users = from e in userHandler.UsersHandlers.Values
                        where e.Id != userHandler.Id && e.IsAlive()
                        orderby e.Id, e.UserName
                        select e.ToBytes();
            int total_users_bytes = 0;
            foreach (var e in bytes_users)
            {
                total_users_bytes += e.Length;
            }

            //Console.WriteLine($"I have {bytes_users.Count()} other users connected");
            byte[] n_users_bytes = BitConverter.GetBytes((Int16)bytes_users.Count());

            byte[] cmd = Encoding.UTF8.GetBytes(NomCommande.OUS.ToString());
            byte[] length_bytes = BitConverter.GetBytes((Int16)(n_users_bytes.Length + total_users_bytes));
            

            byte[] response = new byte[cmd.Length + length_bytes.Length + n_users_bytes.Length + total_users_bytes];

            int compt = 0;
            cmd.CopyTo(response, compt); compt += cmd.Length;
            length_bytes.CopyTo(response, compt); compt += length_bytes.Length;
            n_users_bytes.CopyTo(response, compt); compt += n_users_bytes.Length;
            foreach(var e in bytes_users)
            {
                e.CopyTo(response, compt);
                compt += e.Length;
            }

            string cmd_string = System.Text.Encoding.UTF8.GetString(response, 0, response.Length);
            Console.WriteLine($" >> The client {userHandler.UserName} Id {userHandler.Id} asked for all connected user");
            //Console.WriteLine($" >> packet sent {cmd_string}");
            return response;
        }

        public static byte[] ReceivePositionPlayed(byte[] bytes, UserHandler userHandler)
        {
            Vector3 position = Serialization.DeserializationPositionPlayed(bytes);
            userHandler.Game.Play(position, userHandler.Id);
            return new byte[0];
        }

        public static byte[] SendGameBoard(byte[] bytes, UserHandler userHandler)
        {
            byte[] bytesGame = Serialization.SerializationMatchStatus(userHandler.Game);
            if (!(userHandler.Game.Mode == GameMode.Player1 || userHandler.Game.Mode == GameMode.Player2))
            {
                userHandler.Game = null;
            }

            byte[] response = serializationMessage(bytesGame, NomCommande.DGB);
            return response;
        }

        public static byte[] TransferMatchRequest(byte[] bytes, UserHandler userHandler)
        {
            int id = BitConverter.ToInt16(bytes, 0);

            return new byte[0];
        }

        // A supprimer !
        public static void SendMessage(NetworkStream stream, string message)
        {
            //command in bytes
            var cmd = Encoding.UTF8.GetBytes(NomCommande.MSG.ToString());
            //length of the content in bytes
            var message_length = BitConverter.GetBytes((Int16)message.Length);
            //content in bytes
            var message_bytes = Encoding.UTF8.GetBytes(message);


            byte[] msg = new byte[cmd.Length + message_length.Length + message_bytes.Length];

            //command
            cmd.CopyTo(msg, 0);
            //length to follow
            message_length.CopyTo(msg, cmd.Length);
            //content
            message_bytes.CopyTo(msg, cmd.Length + message_length.Length);


            //envoie de la requête
            stream.Write(msg, 0, msg.Length);
        }
        
    }
}
