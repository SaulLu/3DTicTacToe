﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using MyClient.ModelGame;
using MyClient.Models;
using UnityEngine;

namespace MyClient.Functions
{
    public enum NomCommande
    {
        MSG, //message
        USN, //username
        OUS, //other users
        MRQ, //match request
        NPP, //New position played 
        DGB, // game board
        GRR, // request response
        RGR, // recieve game request
        NDC, // notification opponent disconnected
        PNG // Ping
        //RQS, //

    }

    public class Messaging
    {
        //Serialization

        private static byte[] serializationMessage(NomCommande nomCommande)
        {
            
            var cmd = Encoding.UTF8.GetBytes(nomCommande.ToString());
            var message_length = BitConverter.GetBytes((Int16)0);

            byte[] msg = new byte[cmd.Length + message_length.Length];
            
            cmd.CopyTo(msg, 0);
            message_length.CopyTo(msg, cmd.Length);
            
            return msg;
        }

        private static byte[] serializationMessage(string message, NomCommande nomCommande)
        {
            var cmd = Encoding.UTF8.GetBytes(nomCommande.ToString());
            var message_bytes = Encoding.UTF8.GetBytes(message);
            var message_length = BitConverter.GetBytes((Int16)message_bytes.Length);
            

            byte[] msg = new byte[cmd.Length + message_length.Length + message_bytes.Length];
            
            cmd.CopyTo(msg, 0);
            message_length.CopyTo(msg, cmd.Length);
            message_bytes.CopyTo(msg, cmd.Length + message_length.Length);
            
            return msg;
        }

        private static byte[] serializationMessage(byte[] message_bytes, NomCommande nomCommande)
        {
            var cmd = Encoding.UTF8.GetBytes(nomCommande.ToString());
            var message_length = BitConverter.GetBytes((Int16)message_bytes.Length);

            byte[] msg = new byte[cmd.Length + message_length.Length + message_bytes.Length];
            
            cmd.CopyTo(msg, 0);
            message_length.CopyTo(msg, cmd.Length);
            message_bytes.CopyTo(msg, cmd.Length + message_length.Length);
            
            return msg;
        }

        private static byte[] serializationResponseOpponent(int idOpponent, bool response)
        {
            byte[] idOpponent_bytes = BitConverter.GetBytes((Int16)idOpponent);
            byte[] response_bytes = BitConverter.GetBytes(response);
            byte[] message = new byte[idOpponent_bytes.Length + response_bytes.Length];
            idOpponent_bytes.CopyTo(message, 0);
            response_bytes.CopyTo(message, idOpponent_bytes.Length);
            return message;
        }

        private static Tuple<int, bool> deserializationResponseOpponent(byte[] bytes)
        {
            int byte_compt = 0;
            int idOpponent = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
            bool response = BitConverter.ToBoolean(bytes, byte_compt);
            return Tuple.Create(idOpponent, response);
        }

        private static User deserializationReceiveGameRequest(byte[] bytes)
        {
            int byte_compt = 0;
            int user_id = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
            int userName_length = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
            string userName = System.Text.Encoding.UTF8.GetString(bytes, byte_compt, userName_length); byte_compt += userName_length;
            return new User(user_id, userName);
        }

        // General commands
        public static void SendPing(Client client)
        {
            byte[] msg = serializationMessage(NomCommande.PNG);
            client.StreamWrite(msg);
        }

        public static void RecievePing(byte[] bytes, Client client)
        {
            //client.LogWriter.Write("ping recieved from the server");
        }

        public static void RecieveMessage(byte[] bytes, Client client)
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            client.LogWriter.Write("message recieved from the server: " + message);
        }

        public static void AskOtherUsers(Client client)
        {
            byte[] msg = serializationMessage(NomCommande.OUS);
            client.StreamWrite(msg);
        }

        public static void RecieveOtherUsers(byte[] bytes, Client client)
        {
            int n_users = BitConverter.ToInt16(bytes, 0);

            Dictionary<int, User> newConnectedUsers = new Dictionary<int, User>();
            int byte_compt = 2;
            List<User> listUsers = new List<User>();
            for (int i = 0; i < n_users; i++)
            {
                int user_id = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
                int userName_length = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
                string userName = System.Text.Encoding.UTF8.GetString(bytes, byte_compt, userName_length); byte_compt += userName_length;
                User user = new User(user_id, userName);
                newConnectedUsers[user_id] = user;
                listUsers.Add(user);
            }
            client.RaiseOpponentListUpdated(listUsers);

            client.ConnectedUsers = new Dictionary<int, User>(newConnectedUsers);
        }

        public static void SendMessage(Client client, string message)
        {
            byte[] msg = serializationMessage(message, NomCommande.MSG);
            client.StreamWrite(msg);
        }

        public static void SendUserName(Client client, string userName)
        {
            Debug.Log("Sending new username: " + userName);
            byte[] msg = serializationMessage(userName, NomCommande.USN);
            client.StreamWrite(msg);
        }

        // Game Requests commands

        public static void RequestMatch(Client client, int id)
        {
            byte[] msg = serializationMessage(BitConverter.GetBytes((Int16)id), NomCommande.MRQ);
            client.StreamWrite(msg);
        }

        public static void RecieveGameRequestStatus(byte[] bytes, Client client)
        {
            Tuple<int, bool> tuple = deserializationResponseOpponent(bytes);
            int idOpponent = tuple.Item1;
            bool response = tuple.Item2;
            var user = client.ConnectedUsers[idOpponent];
            if (response)
            {
                client.Opponent = user;
            }
            var status = response ? MatchRequestEventArgs.EStatus.Accepted : MatchRequestEventArgs.EStatus.Declined;
            client.RaiseMatchRequestUpdated(new MatchRequestEventArgs(user, status));
        }

        public static void RecieveGameRequest(byte[] bytes, Client client)
        {
            int byte_compt = 0;
            int user_id = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
            int userName_length = BitConverter.ToInt16(bytes, byte_compt); byte_compt += 2;
            string userName = System.Text.Encoding.UTF8.GetString(bytes, byte_compt, userName_length); byte_compt += userName_length;
            User user = new User(user_id, userName);
            if (!(client.ConnectedUsers.ContainsKey(user_id)))
            {
                client.ConnectedUsers[user_id]= user;
            }
            client.gameRequestsRecieved[user_id] = user;
            client.RaiseMatchRequestUpdated(new MatchRequestEventArgs(user, MatchRequestEventArgs.EStatus.New));
        }

        public static void SendGameRequestResponse(Client client, int idOpponent, bool response)
        {
            AskOtherUsers(client); //probablement a supprimer maintenant que le dictionnaire ConnectedUsers est mis a jour lors de l'appel de la methode RecieveGameRequest

            if (response)
            {
                byte[] bytes = serializationMessage(serializationResponseOpponent(idOpponent, response), NomCommande.GRR);
                client.StreamWrite(bytes);

                client.Opponent = client.ConnectedUsers[idOpponent];
                client.gameRequestsRecieved.Remove(idOpponent);
                var itemsToRemove = client.gameRequestsRecieved.ToArray();
                foreach (var opponent in itemsToRemove)
                {
                    bytes = serializationMessage(serializationResponseOpponent(opponent.Key, !response), NomCommande.GRR);
                    client.StreamWrite(bytes);
                    client.gameRequestsRecieved.Remove(opponent.Key);
                }
            }
            else
            {
                byte[] bytes = serializationMessage(serializationResponseOpponent(idOpponent, response), NomCommande.GRR);
                client.StreamWrite(bytes);
                client.gameRequestsRecieved.Remove(idOpponent);
            }
        }


        // In-game commands
        public static void SendPositionPlayer(Client client, System.Numerics.Vector3 position)
        {
            byte[] positionBytes = Serialization.SerializationPositionPlayed(position);
            byte[] msg = serializationMessage(positionBytes, NomCommande.NPP);
            client.StreamWrite(msg);
        }

        public static void AskGameBoard(Client client)
        {
            byte[] msg = serializationMessage(NomCommande.DGB);
            client.StreamWrite(msg);
        }

        public static void RecieveGameBoard(byte[] bytes, Client client)
        {
            client.GameClient = Serialization.DeserializationMatchStatus(bytes);
        }

        public static void RecieveOpponentDisconnection(byte[] bytes, Client client)
        {
            Debug.Log("RaiseOpponentDisconnected");
            client.RaiseOpponentDisconnected();
        }
    }
}
