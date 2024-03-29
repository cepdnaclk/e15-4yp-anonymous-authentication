﻿using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;

using System.Diagnostics;

using System.Runtime.InteropServices;

using Cryptography;
using Authentication;
using TCP;
using dtls_client;
using PairStream;
using dtls_server;

using SecretSaring;


namespace superpeer_network
{
    class Program
    {
        static Dictionary<string, IPEndPoint> peers;
        static Dictionary<IPEndPoint, SslStream> superpeer_neighbours;

        static Dictionary<string, IPEndPoint> message_buffer;
        static Dictionary<IPEndPoint, IPEndPoint> message_tunnel;

        static Dictionary<IPEndPoint, IPEndPoint> auth_tunnel;
        static Dictionary<IPEndPoint, IPEndPoint> auth_tunnel_rev;
        static Dictionary<IPEndPoint, bool> pending_requests;

        static Dictionary<string, string> reply_buffer;
        static Dictionary<IPEndPoint, PublicKeyCoordinates> client_keys;
        static Dictionary<IPEndPoint, SslStream> req_stream;

        static X509Certificate2 server_cert;
        static IPAddress local_ip;
        static int local_port;
        static string server_ip;
        static int server_port;

        static string neighbour_ip;
        static int neighbour_port;

        static TcpListener server;

        static IPEndPoint ipLocalEndPoint;

        static List<IPEndPoint> exit_neighbours;
        static List<IPEndPoint> change_neighbours;

        static Dictionary<String, String> shared_keys;
        static List<String> rec_keys;
        static List<string> listner_buffer;
        static int hop_count = 2;
        static int flood_phases = 2;

        static int n;
        static int r;

        static string local_ip_str;

        static bool verbose = false;


        public static void insert_peers(string[] new_peers)
        {
            for (int i = 0; i < new_peers.Length - 1; ++i)
            {
                string[] temp_split = new_peers[i].Split(':');
                shared_keys[temp_split[0]] = temp_split[1];
            }
        }

        static void connect_neighbour(IPEndPoint neighbour)
        {
            Console.WriteLine("Connecting new neighbour");
            server.Server.Shutdown(SocketShutdown.Both);
            server.Stop();

            server_ip = neighbour.Address.ToString();
            server_port = neighbour.Port;

            neighbour_ip = null;
            neighbour_port = -1;

            init_connection(server_ip, server_port);

            if (neighbour_ip != null)
            {
                init_connection(neighbour_ip, neighbour_port);
            }

            server.Start();
            new Thread(() => handle_connections()).Start();
        }

        static void disconnect_neighbour(IPEndPoint neighbour)
        {
            Console.WriteLine("Disconnecting: " + neighbour.ToString());

            SslStream sslStream = superpeer_neighbours[neighbour];
            superpeer_neighbours.Remove(neighbour);
            sslStream.Close();
        }

        static void listen_neighbours(KeyValuePair<IPEndPoint, SslStream> neighbour)
        {
            Console.WriteLine("A listen thread created");
            string response = "";
            string[] temp_split;
            IPEndPoint ip = neighbour.Key;
            SslStream sslStream = neighbour.Value;
            while (true)
            {
                try
                {
                    response = TCPCommunication.recieve_message_tcp(sslStream);
                    Console.Write($"Receive({ip}): ");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(response);
                    Console.ResetColor();

                    if (String.Compare(response, "END") == 0)
                    {
                        TCPCommunication.send_message_tcp(sslStream, "ACCEPT");
                        disconnect_neighbour(ip);

                        break;
                        /*disconnect_neighbour = neighbour;
                        break;*/
                    }

                    /*else if (String.Compare(response, "ACCEPT") == 0)
                    {
                        // TCPCommunication.send_message_tcp(sslStream, "ACCEPT_END");

                        disconnect_neighbour(ip);

                        break;
                        /*disconnect_neighbour = neighbour;
                        break;*//*
                    }*/
                    else if (String.Compare(response, "EXIT") == 0)
                    {

                        recieve_peers(sslStream);
                        response = TCPCommunication.recieve_message_tcp(sslStream);

                        Console.WriteLine(response);

                        temp_split = response.Split(':');
                        string neighbour_ip = temp_split[0];
                        int neighbour_port = Int32.Parse(temp_split[1]);
                        string condition = temp_split[2];

                        TCPCommunication.send_message_tcp(sslStream, "ACCEPT");
                        if (String.Compare(condition, "N") == 0)
                        {
                            Console.WriteLine("Here");
                            connect_neighbour(new IPEndPoint(IPAddress.Parse(neighbour_ip), neighbour_port));
                        }
                        disconnect_neighbour(ip);
                        break;
                    }
                    else if (String.Compare(response, "ACCEPT") == 0)
                    {
                        disconnect_neighbour(ip);
                        break;
                    }
                    else
                    {
                        temp_split = response.Split(':');

                        string op_code = temp_split[0];
                        string data0 = (temp_split.Length >= 2) ? temp_split[1] : "";
                        string data1 = (temp_split.Length >= 3) ? temp_split[2] : "";
                        string data2 = (temp_split.Length >= 4) ? temp_split[3] : "";

                        if (String.Compare(op_code, "SEARCH") == 0)
                        {
                            Console.WriteLine("search request recieved for: " + data0);
                            Console.WriteLine("Hop count: " + data2);
                            if (peers.ContainsKey(data0))
                            {
                                Console.WriteLine("Key is found");
                                //peers.Remove(data0);
                                if (!(String.Compare(data1, "NONE") == 0))
                                {
                                    peers[data1] = null;
                                    Console.WriteLine("add peer " + data1);
                                }
                                TCPCommunication.send_message_tcp(sslStream, "FOUND:" + data0 + ":" + local_ip.ToString() + ":" + local_port);
                                Console.WriteLine("Sent");
                            }
                            else
                            {
                                if (String.Compare(data2, "0") != 0)
                                {
                                    foreach (var dest in superpeer_neighbours)
                                    {
                                        if (!dest.Key.Equals(ip))
                                        {
                                            Console.WriteLine("Adding search to buffer for: " + dest.Key);
                                            message_tunnel[dest.Key] = ip;
                                            new Thread(() => remove_tunnel(dest.Key)).Start();
                                            message_buffer[-1 + ":SEARCH:" + data0 + ":" + data1 + ":" + data2] = dest.Key;
                                        }
                                    }
                                }
                            }
                        }
                        else if (String.Compare(op_code, "SEARCH_P") == 0)
                        {
                            Console.WriteLine("search request recieved for: " + data0);
                            Console.WriteLine("Hop count: " + data1);
                            if (peer_exists(data0))
                            {
                                Console.WriteLine("Key is found");
                                IPEndPoint listen_ip = peer_get_ip(data0);
                                Console.WriteLine("listner: " + listen_ip);
                                if (pending_requests[listen_ip])
                                {
                                    Console.WriteLine("loop stop");
                                    auth_tunnel_rev[ip] = null;
                                    TCPCommunication.send_message_tcp(sslStream, "FOUND_P:" + data0 + ":" + local_ip.ToString() + ":" + local_port);

                                    //req_stream[receiver_ip] = sslStream;
                                    pending_requests[listen_ip] = false;
                                }
                            }
                            else
                            {
                                if (String.Compare(data1, "0") != 0)
                                {
                                    foreach (var dest in superpeer_neighbours)
                                    {
                                        if (!dest.Key.Equals(ip))
                                        {
                                            Console.WriteLine("Adding search to buffer for: " + dest.Key);
                                            message_tunnel[dest.Key] = ip;
                                            new Thread(() => remove_tunnel(dest.Key)).Start();
                                            message_buffer[-1 + ":SEARCH_P:" + data0 + ":" + data1] = dest.Key;
                                        }
                                    }
                                }
                            }
                        }
                        else if (String.Compare(op_code, "FOUND_P") == 0)
                        {
                            if (message_tunnel.ContainsKey(ip))
                            {
                                if (message_tunnel[ip] != null)
                                {
                                    Console.WriteLine("Tunnel exists for: " + neighbour + "->" + message_tunnel[ip]);
                                    TCPCommunication.send_message_tcp(superpeer_neighbours[message_tunnel[ip]], response);
                                    auth_tunnel[ip] = message_tunnel[ip];
                                    auth_tunnel_rev[message_tunnel[ip]] = ip;
                                    message_tunnel.Remove(ip);

                                }
                                else
                                {
                                    Console.WriteLine("key: " + data0);
                                    Console.WriteLine("Key exists in : " + data1 + ":" + data2);
                                    reply_buffer[data0] = "FOUND:" + data1 + ":" + data2;
                                    auth_tunnel[ip] = message_tunnel[ip];
                                    message_tunnel.Remove(ip);
                                }
                            }
                            else
                            {
                                Console.WriteLine("Tunnel does not exists");
                            }

                        }
                        else if (String.Compare(op_code, "AUTH_P") == 0)
                        {
                            if (auth_tunnel_rev.ContainsKey(ip))
                            {
                                if (auth_tunnel_rev[ip] != null)
                                {
                                    Console.WriteLine("Auth tunnel exists for: " + neighbour + "->" + auth_tunnel_rev[ip]);
                                    TCPCommunication.send_message_tcp(superpeer_neighbours[auth_tunnel_rev[ip]], response);
                                }
                                else
                                {
                                    Console.WriteLine("Auth dest found");
                                    listner_buffer.Add(data0);
                                }
                            }
                            else
                            {
                                Console.WriteLine("Tunnel does not exists");
                            }

                        }
                        else if (String.Compare(op_code, "AUTH_R") == 0)
                        {
                            if (auth_tunnel.ContainsKey(ip))
                            {
                                if (auth_tunnel[ip] != null)
                                {
                                    Console.WriteLine("Auth Reply tunnel exists for: " + neighbour + "->" + auth_tunnel[ip]);
                                    TCPCommunication.send_message_tcp(superpeer_neighbours[auth_tunnel[ip]], response);
                                }
                                else
                                {
                                    listner_buffer.Add(data0);
                                    Console.WriteLine("Auth dest found");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Tunnel does not exists");
                            }

                        }
                        else if (String.Compare(op_code, "FOUND") == 0)
                        {
                            if (message_tunnel.ContainsKey(ip))
                            {
                                if (message_tunnel[ip] != null)
                                {
                                    Console.WriteLine("Tunnel exists for: " + neighbour + "->" + message_tunnel[ip]);
                                    TCPCommunication.send_message_tcp(superpeer_neighbours[message_tunnel[ip]], response);
                                    message_tunnel.Remove(ip);
                                }
                                else
                                {
                                    Console.WriteLine("key: " + data0);
                                    Console.WriteLine("Key exists in : " + data1 + ":" + data2);
                                    reply_buffer[data0] = "FOUND:" + data1 + ":" + data2;
                                    message_tunnel.Remove(ip);

                                }
                            }
                            else
                            {
                                Console.WriteLine("Tunnel does not exists");
                            }

                        }
                        else if (String.Compare(op_code, "REG") == 0)
                        {
                            Console.WriteLine(response);
                            string key = temp_split[1];
                            if (shared_keys.ContainsKey(key))
                            {
                                Console.WriteLine("Key already exists");
                                foreach (var dest in superpeer_neighbours)
                                {
                                    if (!dest.Key.Equals(ip))
                                    {
                                        Console.WriteLine("Adding reg message to buffer for: " + dest.Key);
                                        message_buffer[-1 + ":" + response] = dest.Key;
                                    }
                                }
                            }
                            else
                            {

                                shared_keys[key] = temp_split[2];
                                Console.WriteLine();
                                Console.WriteLine($"key for {key} stored: " + temp_split[2]);
                                Console.WriteLine();
                                if (temp_split.Length > 3)
                                {
                                    string full_msg = temp_split[0] + ":" + temp_split[1];

                                    for (int i = 3; i < temp_split.Length; ++i)
                                    {
                                        full_msg += ":" + temp_split[i];
                                    }

                                    foreach (var dest in superpeer_neighbours)
                                    {
                                        if (!dest.Key.Equals(ip))
                                        {
                                            Console.WriteLine("Adding reg message to buffer for: " + dest.Key);
                                            message_buffer[-1 + ":" + full_msg] = dest.Key;
                                        }
                                    }
                                }

                            }

                        }
                        else if (String.Compare(op_code, "REQ") == 0)
                        {
                            if (shared_keys.ContainsKey(data0))
                            {
                                IPAddress ipAddress = IPAddress.Parse(local_ip_str);
                                Random random = new Random();
                                int port = random.Next(2000, 4000);
                                IPEndPoint ipLocalEndPoint = new IPEndPoint(ipAddress, port);

                                //Connect to server
                                TcpClient client = new TcpClient(ipLocalEndPoint);
                                try
                                {
                                    client.Connect(data1, Int32.Parse(data2));

                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine("try again!!!");
                                    Thread.Sleep(1000);
                                    client.Connect(data1, Int32.Parse(data2));

                                }
                                SslStream stream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                                authenticate_server(stream);

                                TCPCommunication.send_message_tcp(stream, data0 + ":" + shared_keys[data0]);
                            }
                            foreach (var dest in superpeer_neighbours)
                            {
                                if (!dest.Key.Equals(ip))
                                {
                                    Console.WriteLine("Adding req message to buffer for: " + dest.Key);
                                    message_buffer[-1 + ":" + response] = dest.Key;
                                }
                            }


                        }
                    }
                }
                catch (System.InvalidOperationException e)
                {
                    Console.WriteLine("Invalid operation");
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception");
                    break;
                }
            }
            Console.WriteLine("Listen thread exit");
        }

        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None || sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
                return true;

            Console.WriteLine("Certificate error: {0}", sslPolicyErrors);

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        static void authenticate_server(SslStream sslStream)
        {
            try
            {
                sslStream.AuthenticateAsClient("test", null, SslProtocols.Tls13, true);
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine("Exception: {0}", e.Message);
                if (e.InnerException != null)
                {
                    Console.WriteLine("Inner exception: {0}", e.InnerException.Message);
                }
                Console.WriteLine("Authentication failed - closing the connection.");
                sslStream.Close();
                return;
            }
        }

        static void handle_neighbour(int index)
        {
            //new Thread(() => listen_neighbours()).Start();
            bool exit_neighbour = false;
            //bool break_loop = false;
            while (true)
            {
                if (exit_neighbours.Count != 0)
                {
                    Thread.Sleep(500);
                    foreach (IPEndPoint neighbour in exit_neighbours.ToArray())
                    {
                        Console.WriteLine("Disconnecting neighbour: " + neighbour.ToString());
                        TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], "EXIT");
                        if (!exit_neighbour)
                        {
                            transfer_peers(superpeer_neighbours[neighbour], (superpeer_neighbours.Count));
                            TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], exit_neighbours[0].ToString() + ":Y");
                            exit_neighbour = true;
                        }
                        else
                        {
                            transfer_peers(superpeer_neighbours[neighbour], (superpeer_neighbours.Count));
                            TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], exit_neighbours[0].ToString() + ":N");
                            //break_loop = true;
                        }
                    }
                    exit_neighbours.Clear();
                    break;
                }

                if (change_neighbours.Count != 0)
                {
                    foreach (var neighbour in change_neighbours)
                    {
                        //Console.WriteLine("Disconnecting neighbour: " + neighbour.ToString());
                        message_buffer[-1 + ":END"] = neighbour;
                        //TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], "END");
                    }
                    change_neighbours.Clear();
                }
                if (superpeer_neighbours.Count != 0)
                {
                    //Console.WriteLine("Neighbours(Send): " + superpeer_neighbours.Count);
                    List<IPEndPoint> superpeer_neighbours_list = null;
                    try
                    {
                        superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                    }
                    catch (System.ArgumentException e)
                    {
                        //Console.WriteLine("A");
                        Console.WriteLine("neighbours have changed");
                        Thread.Sleep(100);
                        continue;
                    }
                    catch (System.IndexOutOfRangeException e)
                    {
                        //Console.WriteLine(e);
                        Console.WriteLine("neighbours have changed");
                        Thread.Sleep(100);
                        continue;
                    }
                    foreach (var neighbour in superpeer_neighbours_list)
                    {
                        if (message_buffer.Count != 0)
                        {
                            try
                            {

                                List<string> message_buffer_list = new List<string>(message_buffer.Keys);

                                //message_itr.MoveNext();
                                string message_full = message_buffer_list[0];
                                Console.WriteLine(message_full);
                                string[] temp_split = message_full.Split(':');

                                string message = (temp_split.Length >= 1) ? temp_split[1] : "";
                                string data0 = "";
                                string data1 = "";
                                string data2 = "";
                                int hops = -1;

                                if (String.Compare(message, "REG") == 0)
                                {

                                    string full_msg = message + ":" + temp_split[2];

                                    for (int i = 3; i < temp_split.Length; ++i)
                                    {
                                        full_msg += ":" + temp_split[i];
                                    }

                                    IPEndPoint destination_ip = message_buffer[message_full];

                                    if (destination_ip == neighbour)
                                    {
                                        Console.WriteLine($"Sending({neighbour}): keys");
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.ResetColor();
                                        TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], full_msg);

                                        message_buffer.Remove(message_full);
                                        //new Thread(() => remove_tunnel(neighbour)).Start();
                                    }
                                }
                                else if (String.Compare(message, "REQ") == 0)
                                {
                                    data0 = temp_split[2];
                                    data1 = temp_split[3];
                                    data2 = temp_split[4];
                                    string full_msg = message + ":" + data0 + ":" + data1 + ":" + data2;
                                    IPEndPoint destination_ip = message_buffer[message_full];
                                    if (destination_ip == neighbour)
                                    {
                                        Console.Write($"Sending({neighbour}): ");
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"{message}");
                                        Console.ResetColor();
                                        TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], full_msg);
                                        message_buffer.Remove(message_full);
                                    }
                                }
                                else if (String.Compare(message, "SEARCH_P") == 0)
                                {
                                    data0 = temp_split[2];
                                    hops = (Int32.Parse((temp_split.Length > 2) ? temp_split[3] : "0")) - 1;

                                    IPEndPoint destination_ip = message_buffer[message_full];
                                    if (destination_ip == neighbour)
                                    {
                                        Console.Write($"Sending({neighbour}): ");
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"{message}");
                                        Console.ResetColor();
                                        TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], message + ":" + data0 + ":" + hops);
                                        message_buffer.Remove(message_full);
                                    }
                                }
                                else if (String.Compare(message, "AUTH_P") == 0)
                                {
                                    data0 = temp_split[2];
                                    Console.WriteLine(data0);
                                    IPEndPoint destination_ip = message_buffer[message_full];
                                    if (destination_ip.Equals(neighbour))
                                    {
                                        Console.Write($"Sending({neighbour}): ");
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"{message}");
                                        Console.ResetColor();
                                        TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], message + ":" + data0);
                                        message_buffer.Remove(message_full);
                                    }
                                }
                                else if (String.Compare(message, "AUTH_R") == 0)
                                {
                                    data0 = temp_split[2];
                                    Console.WriteLine("h: " + data0);
                                    IPEndPoint destination_ip = message_buffer[message_full];
                                    if (destination_ip.Equals(neighbour))
                                    {
                                        Console.Write($"Sending({neighbour}): ");
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"{message}");
                                        Console.ResetColor();
                                        TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], message + ":" + data0);
                                        message_buffer.Remove(message_full);
                                    }
                                    else
                                    {
                                        Console.WriteLine("dest: " + destination_ip);
                                        Console.WriteLine("neigh: " + neighbour);
                                    }
                                }
                                else
                                {
                                    data0 = (temp_split.Length > 2) ? temp_split[2] : "";
                                    data1 = (temp_split.Length > 2) ? temp_split[3] : "";
                                    hops = (Int32.Parse((temp_split.Length > 2) ? temp_split[4] : "0")) - 1;

                                    //string message = message_full.Split(':')[1];

                                    //Get corresponding destination of the message
                                    IPEndPoint destination_ip = message_buffer[message_full];

                                    if (destination_ip == neighbour)
                                    {

                                        //message_tunnel[neighbour] = restrict_ip;
                                        //TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], "SEARCH:" + message);
                                        if (data0 != "")
                                        {
                                            Console.Write($"Sending({neighbour}): ");
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"{message}: {data0}");
                                            Console.ResetColor();
                                            TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], message + ":" + data0 + ":" + data1 + ":" + hops);
                                        }
                                        else
                                        {
                                            Console.Write($"Sending({neighbour}): ");
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"{message}");
                                            Console.ResetColor();
                                            TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], message);
                                        }
                                        message_buffer.Remove(message_full);
                                        //new Thread(() => remove_tunnel(neighbour)).Start();
                                    }
                                }

                                /*else
                                {
                                    Console.WriteLine("sending: " + count);
                                    TCPCommunication.send_message_tcp(superpeer_neighbours[neighbour], "HELLO(" + count++ + ")");
                                }*/
                            }
                            catch (System.InvalidOperationException e)
                            {
                                //Console.WriteLine(e);
                                Console.WriteLine("neighbours have changed");
                                Thread.Sleep(100);
                                break;
                            }
                            catch (Exception e)
                            {
                                //Console.WriteLine(e);
                                Console.WriteLine("neighbours have changed");
                                Thread.Sleep(100);
                                break;
                            }
                        }
                    }

                    //Thread.Sleep(2000);
                }
            }
            Console.WriteLine("Send thread Closed");
        }

        static void remove_tunnel(IPEndPoint ip)
        {
            Thread.Sleep(5000);
            if (message_tunnel.ContainsKey(ip))
            {
                Console.WriteLine("tunnel " + ip.ToString() + " " + message_tunnel[ip] + " is removed");
                message_tunnel.Remove(ip);

            }
        }


        //Trap ctrl+c to distribute peers among neighbour upon exit
        private static void On_exit(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Exiting");
            e.Cancel = true;

            List<IPEndPoint> superpeer_neighbours_list = null;
            try
            {
                superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
            }
            catch (System.ArgumentException)
            {
                Console.WriteLine("neighbours have changed");
                Thread.Sleep(100);
            }
            foreach (IPEndPoint neighbour in superpeer_neighbours_list)
            {
                exit_neighbours.Add(neighbour);
            }

            Thread.Sleep(4000);

            // server.Server.Disconnect(true);
            // server.Server.Close();

            System.Environment.Exit(1);

        }

        private static void init_connection(string ip, int port)
        {
            ipLocalEndPoint = new IPEndPoint(local_ip, local_port);

            //Initiate connection with neighbour (Get 1/3 of neighbours peers)
            TcpClient client = new TcpClient(ipLocalEndPoint);

            client.Connect(ip, port);
            SslStream sslStream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(SSLValidation.ValidateServerCertificate), null);
            establish_connection(sslStream);

            superpeer_neighbours[new IPEndPoint(IPAddress.Parse(ip), port)] = sslStream;

            KeyValuePair<IPEndPoint, SslStream> new_neighbour = new KeyValuePair<IPEndPoint, SslStream>(new IPEndPoint(IPAddress.Parse(ip), port), sslStream);
            new Thread(() => listen_neighbours(new_neighbour)).Start();

            /* sslStream.Close();
             client.Close();*/

        }

        static void establish_connection(SslStream sslStream)
        {
            SSLValidation.authenticate_client(sslStream);

            string response;
            string[] temp_split;

            TCPCommunication.send_message_tcp(sslStream, "HELLO_S");
            TCPCommunication.send_message_tcp(sslStream, (server_ip + ":" + server_port));

            response = TCPCommunication.recieve_message_tcp(sslStream);
            Console.WriteLine(response);
            if (String.Compare(response, "CONNECT_S") == 0)
            {
                recieve_peers(sslStream);
                response = TCPCommunication.recieve_message_tcp(sslStream);
                Console.WriteLine(response);
            }
            else if (String.Compare(response, "CHANGE_S") == 0)
            {
                recieve_peers(sslStream);
                response = TCPCommunication.recieve_message_tcp(sslStream);
                Console.WriteLine(response);

                temp_split = response.Split(':');
                neighbour_ip = temp_split[0];
                neighbour_port = Int32.Parse(temp_split[1]);

                //new IPEndPoint(IPAddress.Parse(neighbour_ip), neighbour_port);

                response = TCPCommunication.recieve_message_tcp(sslStream);
                Console.WriteLine(response);

            }
            else
            {
                Console.WriteLine("Unrecognized message");
            }
        }

        static void hello_neighbour()
        {
            while (true)
            {
                int count = 0;
                List<IPEndPoint> superpeer_neighbours_list = null;
                try
                {
                    superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                }
                catch (System.ArgumentException e)
                {
                    Console.WriteLine("neighbours have changed");
                    Thread.Sleep(100);
                    continue;
                }
                foreach (IPEndPoint neighbour in superpeer_neighbours_list)
                {
                    message_buffer[count + ":HELLO"] = neighbour;
                    count++;
                }
                Thread.Sleep(5000);
            }
            Console.WriteLine("Exit hello_neighbour thread");
        }

        static void Main(string[] args)
        {
            /*
            try
            {
                n = Int32.Parse(args[0]);
                r = Int32.Parse(args[1]);
            }
            catch (Exception e)
            {
                Console.WriteLine("Incorrect format");
                Console.WriteLine("dotnet run <n> <r>");
                return;
            }
            if (args.Length > 2 && args[2] == "-v")
            {
                verbose = true;
            }
            else if (args.Length > 2)
            {
                Console.WriteLine("Incorrect format");
                Console.WriteLine("dotnet run <n> <r> -v");
                return;
            }*/
            local_ip_str = "127.0.0.1";
            //To handle on exit function to distribute peers upon exit
            Console.CancelKeyPress += On_exit;


            neighbour_ip = null;
            neighbour_port = -1;

            //Console.Write("Server ip: ");
            //server_ip = Console.ReadLine();
            server_ip = "127.0.0.1";

            Console.Write("Server port: ");
            server_port = Convert.ToInt32(Console.ReadLine());


            //Initiate database
            superpeer_neighbours = new Dictionary<IPEndPoint, SslStream>();
            peers = new Dictionary<string, IPEndPoint>();
            message_buffer = new Dictionary<string, IPEndPoint>();
            message_tunnel = new Dictionary<IPEndPoint, IPEndPoint>();
            auth_tunnel = new Dictionary<IPEndPoint, IPEndPoint>();
            auth_tunnel_rev = new Dictionary<IPEndPoint, IPEndPoint>();
            reply_buffer = new Dictionary<string, string>();
            pending_requests = new Dictionary<IPEndPoint, bool>();
            req_stream = new Dictionary<IPEndPoint, SslStream>();

            exit_neighbours = new List<IPEndPoint>();
            change_neighbours = new List<IPEndPoint>();
            shared_keys = new Dictionary<String, String>();
            rec_keys = new List<String>();
            listner_buffer = new List<string>();

            client_keys = new Dictionary<IPEndPoint, PublicKeyCoordinates>();

            //Select a random port number
            Random random = new Random();


            //Add ceritificate to the store
            server_cert = new X509Certificate2("../../FYP_certificates/ssl-certificate.pfx", "password", X509KeyStorageFlags.PersistKeySet);
            X509Store store = new X509Store(StoreName.My);
            store.Open(OpenFlags.ReadWrite);
            store.Add(server_cert);

            //Create the local end point(ip+port)
            local_ip = IPAddress.Parse(local_ip_str);
            local_port = random.Next(20000, 40000);

            init_connection(server_ip, server_port);

            if (neighbour_ip != null)
            {
                init_connection(neighbour_ip, neighbour_port);
            }

            new Thread(() => handle_neighbour(1)).Start();
            new Thread(() => hello_neighbour()).Start();

            server = new TcpListener(local_ip, local_port);
            server.Start();

            //Listen to requests
            handle_connections();


        }

        public static void transfer_peers(SslStream sslStream, int divident)
        {
            string reply = "";
            int limit_count = 0;
            int count = 0;
            int peers_count = shared_keys.Count;
            foreach (var pair in shared_keys)
            {
                if (count >= peers_count / divident)
                {
                    break;
                }
                if (limit_count > 5)
                {
                    TCPCommunication.send_message_tcp(sslStream, reply + "Y");
                    reply = "";
                    limit_count = 0;
                }
                reply += pair.Key + ":" + pair.Value + "/";
                shared_keys.Remove(pair.Key);
                count++;
                limit_count++;
            }
            TCPCommunication.send_message_tcp(sslStream, reply + "N");
        }

        static void recieve_peers(SslStream sslStream)
        {
            string delimiter = "Y";
            string response;
            string[] temp_split;
            while (delimiter == "Y")
            {
                response = TCPCommunication.recieve_message_tcp(sslStream);
                Console.WriteLine(response);
                temp_split = response.Split('/');
                insert_peers(temp_split);
                delimiter = temp_split[temp_split.Length - 1];
            }
            Console.WriteLine("Shared keys");
            foreach (var pair in shared_keys)
            {
                Console.WriteLine(pair.Key);
            }

        }
        static void listen_keys(SslStream stream, int port)
        {
            TcpListener key_server = new TcpListener(local_ip, port);

            key_server.Start();
            Console.WriteLine("Waiting for keys");

            Byte[] bytes = new Byte[256];
            string response = "";
            while (true)
            {
                TcpClient client = null;
                try
                {
                    client = key_server.AcceptTcpClient();
                    SslStream sslStream = new SslStream(client.GetStream(), false);
                    sslStream.AuthenticateAsServer(server_cert, clientCertificateRequired: false, SslProtocols.Tls13, checkCertificateRevocation: true);
                    // Read a message from the client.
                    response = TCPCommunication.recieve_message_tcp(sslStream);
                    Console.WriteLine();
                    Console.WriteLine(response);
                    Console.WriteLine();

                    Console.ForegroundColor = ConsoleColor.Red;
                    rec_keys.Add(response);
                    Console.ResetColor();

                }
                catch (TimeoutException e)
                {
                    Console.WriteLine("Time ran out");
                    break;
                }
                catch
                {
                    Console.WriteLine("Exception");
                    break;
                }

            }
            Console.WriteLine("Stop listening to keys");
        }

        static void wait_keys(SslStream stream, int port)
        {

            Thread listen = new Thread(() => listen_keys(stream, port));
            listen.Start();
            Thread.Sleep(8000);
            try
            {
                listen.Abort();
            }
            catch (Exception e)
            {
                Console.WriteLine("Server start error!!");
            }
            finally
            {
                Dictionary<String, HashSet<String>> keys = new Dictionary<String, HashSet<String>>();
                foreach (var key in shared_keys)
                {
                    HashSet<String> temp = new HashSet<String>();
                    keys[key.Key] = temp;
                    temp.Add(key.Value);
                }

                foreach (String key in rec_keys)
                {
                    string[] temp_split = key.Split(":");
                    keys[temp_split[0]].Add(temp_split[1]);
                }
                string reply = "";
                Console.WriteLine("Collected keys: ");
                foreach (var key in keys)
                {
                    string[] shares = new string[2];
                    int i = 0;
                    foreach (string part in key.Value)
                    {
                        shares[i++] = part;
                        if (i == r)
                            break;
                    }

                    var generatedKey = SharesManager.CombineKey(shares);
                    var hexKey = KeyGenerator.GetHexKey(generatedKey);

                    Console.WriteLine(hexKey);
                    reply += hexKey + "/";

                }
                TCPCommunication.send_message_tcp(stream, reply);
                stream.Close();
            }
        }

        static void wait_reply(SslStream sslStream, string receiver_key, string sender_key)
        {
            for (int i = 0; i < flood_phases; ++i)
            {
                Thread.Sleep(3000);
                if (!reply_buffer.ContainsKey(receiver_key))
                {
                    List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                    int count = 0;
                    foreach (var neighbour in superpeer_neighbours_list)
                    {
                        Console.WriteLine("To: " + neighbour);
                        message_tunnel[neighbour] = null;
                        message_buffer[(count++) + ":SEARCH:" + receiver_key + ":" + sender_key + ":" + (hop_count * 2)] = neighbour;
                    }
                    //new Thread(() => wait_reply(sslStream, receiver_key, sender_key)).Start();
                }
                else
                {
                    break;
                }
            }
            //Thread.Sleep(3000);
            if (reply_buffer.ContainsKey(receiver_key))
            {
                //peers[key] = null;
                TCPCommunication.send_message_tcp(sslStream, reply_buffer[receiver_key]);

                reply_buffer.Remove(receiver_key);
                //TCPCommunication.send_message_tcp(sslStream, "FOUND");

                //response = TCPCommunication.recieve_message_tcp(sslStream);
                if (!(String.Compare(sender_key, "NONE") == 0))
                {
                    peers.Remove(sender_key);
                    Console.WriteLine(sender_key + " is removed");
                }
                //TCPCommunication.send_message_tcp(sslStream, "SUCCESS");
                sslStream.Close();

            }
            else
            {
                TCPCommunication.send_message_tcp(sslStream, "NOTFOUND");
                sslStream.Close();
            }

            //sslStream.Close();
        }

        static void wait_reply(SslStream sslStream, string dest_key)
        {
            for (int i = 0; i < flood_phases; ++i)
            {
                Thread.Sleep(3000);
                if (!reply_buffer.ContainsKey(dest_key))
                {
                    List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                    int count = 0;
                    foreach (var neighbour in superpeer_neighbours_list)
                    {
                        Console.WriteLine("To: " + neighbour);
                        message_tunnel[neighbour] = null;
                        message_buffer[(count++) + ":SEARCH_P:" + dest_key + ":" + (hop_count * 2)] = neighbour;
                    }
                    //new Thread(() => wait_reply(sslStream, receiver_key, sender_key)).Start();
                }
                else
                {
                    break;
                }
            }
            //Thread.Sleep(3000);
            if (reply_buffer.ContainsKey(dest_key))
            {
                //peers[key] = null;
                TCPCommunication.send_message_tcp(sslStream, reply_buffer[dest_key]);

                reply_buffer.Remove(dest_key);
                //TCPCommunication.send_message_tcp(sslStream, "FOUND");
                //sslStream.Close();

                byte[] bytes;

                bytes = new byte[16];
                sslStream.Read(bytes, 0, bytes.Length);

                string U = Encoding.Default.GetString(bytes);
                Console.WriteLine("U: " + U);

                var enumerator = auth_tunnel.Keys.GetEnumerator();
                enumerator.MoveNext();

                IPEndPoint tunnel = enumerator.Current;

                Console.WriteLine("Sending auth message to: " + tunnel);

                //TCPCommunication.send_message_tcp(superpeer_neighbours[tunnel], "AUTH_P:" + U);
                message_buffer[0 + ":AUTH_P:" + U] = tunnel;


                while (listner_buffer.Count == 0) ;

                Console.WriteLine("C: " + listner_buffer[0]);

                TCPCommunication.send_message_tcp(sslStream, listner_buffer[0]);
                listner_buffer.RemoveAt(0);

                string msg = TCPCommunication.recieve_message_tcp(sslStream);

                enumerator = auth_tunnel.Keys.GetEnumerator();
                enumerator.MoveNext();

                tunnel = enumerator.Current;

                Console.WriteLine("Sending auth message to: " + tunnel);

                //TCPCommunication.send_message_tcp(superpeer_neighbours[tunnel], "AUTH_P:" + U);
                message_buffer[-1 + ":AUTH_P:" + msg] = tunnel;


            }
            else
            {
                TCPCommunication.send_message_tcp(sslStream, "NOT_FOUND");
                sslStream.Close();
            }

            //sslStream.Close();
        }

        static void print_key(string key)
        {
            for (int i = 0; i < key.Length; ++i)
            {
                for (int j = 3; j < 8; ++j)
                {
                    Console.Write((key[i] * j).ToString("X") + "-");
                }
            }
            Console.WriteLine();
        }

        static bool compare_keys(string key1, string key2)
        {
            string recon_key = "";
            for (int i = 0; i < key2.Length; ++i)
            {
                for (int j = 3; j < 8; ++j)
                {
                    recon_key += (key2[i] * j).ToString("X") + "-";
                }
            }
            if (key1.Equals(recon_key))
            {

                return true;
            }
            return false;
        }

        static IPEndPoint peer_get_ip(string key)
        {
            foreach (var peer in peers)
            {
                if (compare_keys(key, peer.Key))
                {
                    return peer.Value;
                }
            }
            return null;
        }

        static bool peer_exists(string key)
        {
            foreach (var peer in peers)
            {
                if (compare_keys(key, peer.Key))
                {
                    return true;
                }
            }
            return false;
        }




        static void handle_connections()
        {
            Console.WriteLine("Server is starting on port: " + local_port);
            Byte[] bytes = new Byte[256];
            string response;

            while (true)
            {
                TcpClient client = null;
                try
                {
                    client = server.AcceptTcpClient();

                }
                catch
                {
                    Console.WriteLine("Exception");
                    break;
                }
                SslStream sslStream = new SslStream(client.GetStream(), false);
                sslStream.AuthenticateAsServer(server_cert, clientCertificateRequired: false, SslProtocols.Tls13, checkCertificateRevocation: true);
                sslStream.ReadTimeout = 40000;
                sslStream.WriteTimeout = 40000;
                // Read a message from the client.
                response = TCPCommunication.recieve_message_tcp(sslStream);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(response);
                Console.ResetColor();
                if (String.Compare(response, "HELLO_S") == 0)
                {
                    Console.WriteLine(((IPEndPoint)client.Client.RemoteEndPoint) + " is requesting to join superpeer network");

                    response = TCPCommunication.recieve_message_tcp(sslStream);


                    string myIp = local_ip.ToString() + ":" + local_port;
                    Console.WriteLine("Requesting neighbour: " + response);

                    if (myIp == response)
                    {
                        if (superpeer_neighbours.Count < 2)
                        {
                            TCPCommunication.send_message_tcp(sslStream, "CONNECT_S");
                            Console.WriteLine("Sending 1/2 of Peers");
                            transfer_peers(sslStream, 2);

                            superpeer_neighbours[(IPEndPoint)client.Client.RemoteEndPoint] = sslStream;

                            KeyValuePair<IPEndPoint, SslStream> new_neighbour = new KeyValuePair<IPEndPoint, SslStream>((IPEndPoint)client.Client.RemoteEndPoint, sslStream);
                            new Thread(() => listen_neighbours(new_neighbour)).Start();

                            TCPCommunication.send_message_tcp(sslStream, "SUCCESS");

                        }
                        else
                        {
                            TCPCommunication.send_message_tcp(sslStream, "CHANGE_S");
                            Console.WriteLine("Sending 1/3 of Peers");
                            transfer_peers(sslStream, 3);

                            IPEndPoint neighbour;
                            var neighbour_itr = superpeer_neighbours.GetEnumerator();
                            neighbour_itr.MoveNext();

                            neighbour = neighbour_itr.Current.Key;

                            change_neighbours.Add(neighbour);
                            Thread.Sleep(500);

                            TCPCommunication.send_message_tcp(sslStream, neighbour.ToString());
                            superpeer_neighbours[(IPEndPoint)client.Client.RemoteEndPoint] = sslStream;

                            KeyValuePair<IPEndPoint, SslStream> new_neighbour = new KeyValuePair<IPEndPoint, SslStream>((IPEndPoint)client.Client.RemoteEndPoint, sslStream);
                            new Thread(() => listen_neighbours(new_neighbour)).Start();

                            TCPCommunication.send_message_tcp(sslStream, "SUCCESS");
                        }
                    }
                    else
                    {
                        TCPCommunication.send_message_tcp(sslStream, "CONNECT_S");
                        Console.WriteLine("Sending 1/3 of Peers");
                        transfer_peers(sslStream, 3);

                        superpeer_neighbours[(IPEndPoint)client.Client.RemoteEndPoint] = sslStream;

                        KeyValuePair<IPEndPoint, SslStream> new_neighbour = new KeyValuePair<IPEndPoint, SslStream>((IPEndPoint)client.Client.RemoteEndPoint, sslStream);
                        new Thread(() => listen_neighbours(new_neighbour)).Start();

                        TCPCommunication.send_message_tcp(sslStream, "SUCCESS");
                    }


                    Console.WriteLine("My neighbours: ");
                    foreach (var neighbour in superpeer_neighbours)
                    {
                        Console.WriteLine(neighbour.Key);
                    }
                    Console.WriteLine("My peers");
                    foreach (var pair in peers)
                    {
                        Console.WriteLine(pair.Key);
                    }
                }
                else if (String.Compare(response, "REG_P") == 0)
                {
                    Console.WriteLine("Peer registering");
                    Console.WriteLine();
                    Byte[] data = new Byte[6];
                    sslStream.Read(data, 0, data.Length);

                    int players = n;
                    int required = r;

                    var key = KeyGenerator.GenerateDoubleBytesKey(data);
                    var hexKey = KeyGenerator.GetHexKey(key);

                    print_key(hexKey);
                    Console.WriteLine();
                    peers[hexKey] = (IPEndPoint)client.Client.RemoteEndPoint;
                    Console.WriteLine("Registering: " + (IPEndPoint)client.Client.RemoteEndPoint);
                    //Console.WriteLine("key: " + data.Length);

                    //Console.WriteLine("recieved key: " + hexKey);

                    Console.WriteLine("Shares: ");
                    var splitted = SharesManager.SplitKey(key, players, required);
                    for (int i = 0; i < splitted.Length; i++)
                    {
                        Console.WriteLine(splitted[i]);
                        Console.WriteLine();
                    }
                    Console.WriteLine();

                    string key_id = HashString.GetHashString(hexKey);

                    shared_keys[key_id] = splitted[0];

                    List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                    int count = 0;
                    foreach (var neighbour in superpeer_neighbours_list)
                    {
                        message_tunnel[neighbour] = null;
                        message_buffer[(count++) + ":REG:" + (key_id) + ":" + splitted[1] + ":" + splitted[2]] = neighbour;
                    }

                }

                else if (String.Compare(response, "REQ_P") == 0)
                {

                    Console.WriteLine("Requesting public keys");

                    List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                    int count = 0;
                    Random rand = new Random();
                    int port = rand.Next(2000, 4000);
                    new Thread(() => wait_keys(sslStream, port)).Start();
                    Thread.Sleep(500);

                    foreach (var key in shared_keys)
                    {
                        foreach (var neighbour in superpeer_neighbours_list)
                        {
                            message_buffer[(count++) + ":REQ:" + key.Key + ":" + local_ip + ":" + port] = neighbour;
                        }
                    }
                }
                else if (String.Compare(response, "LOCATE_P") == 0)
                {
                    string sender_key = TCPCommunication.recieve_message_tcp(sslStream);
                    if (peers.ContainsKey(sender_key))
                    {
                        TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                        string key = TCPCommunication.recieve_message_tcp(sslStream);

                        if (peers.ContainsKey(key))
                        {
                            TCPCommunication.send_message_tcp(sslStream, "FOUND" + ":" + local_ip + ":" + local_port);
                        }
                        else
                        {
                            List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                            int count = 0;
                            foreach (var neighbour in superpeer_neighbours_list)
                            {
                                message_tunnel[neighbour] = null;
                                message_buffer[(count++) + ":SEARCH:" + key + ":NONE" + ":" + hop_count] = neighbour;
                            }
                            new Thread(() => wait_reply(sslStream, key, "NONE")).Start();
                        }

                    }
                    else
                    {
                        TCPCommunication.send_message_tcp(sslStream, "REJECT");
                        sslStream.Close();
                        client.Close();
                    }
                }
                else if (String.Compare(response, "ANONYM_P") == 0)
                {
                    string sender_key = TCPCommunication.recieve_message_tcp(sslStream);
                    if (peers.ContainsKey(sender_key))
                    {
                        TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                        string new_key = TCPCommunication.recieve_message_tcp(sslStream);

                        peers.Remove(sender_key);
                        peers[new_key] = (IPEndPoint)client.Client.RemoteEndPoint;
                        Console.WriteLine(sender_key + " changed to " + new_key);

                        TCPCommunication.send_message_tcp(sslStream, "SUCCESS");
                    }
                    else
                    {
                        TCPCommunication.send_message_tcp(sslStream, "REJECT");
                        sslStream.Close();
                        client.Close();
                    }
                }
                else if (String.Compare(response, "FIND_P") == 0)
                {
                    //sw = Stopwatch.StartNew();
                    string dest_key = TCPCommunication.recieve_message_tcp(sslStream);
                    //Console.WriteLine(dest_key);
                    if (peer_exists(dest_key))
                    {
                        TCPCommunication.send_message_tcp(sslStream, "FOUND");
                    }
                    else
                    {
                        List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                        int count = 0;
                        foreach (var neighbour in superpeer_neighbours_list)
                        {
                            message_tunnel[neighbour] = null;
                            message_buffer[(count++) + ":SEARCH_P:" + dest_key + ":" + hop_count] = neighbour;
                        }
                        new Thread(() => wait_reply(sslStream, dest_key)).Start();
                    }
                    /*
                    if (peers.ContainsKey(sender_key))
                    {
                        TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                        string receiver_key = TCPCommunication.recieve_message_tcp(sslStream);

                        if (peers.ContainsKey(response))
                        {
                            TCPCommunication.send_message_tcp(sslStream, "FOUND");
                        }
                        else
                        {
                            List<IPEndPoint> superpeer_neighbours_list = new List<IPEndPoint>(superpeer_neighbours.Keys);
                            int count = 0;
                            foreach (var neighbour in superpeer_neighbours_list)
                            {
                                message_tunnel[neighbour] = null;
                                message_buffer[(count++) + ":SEARCH:" + receiver_key + ":" + sender_key + ":" + hop_count] = neighbour;
                            }
                            new Thread(() => wait_reply(sslStream, receiver_key, sender_key)).Start();
                        }
                    }
                    else
                    {
                        TCPCommunication.send_message_tcp(sslStream, "REJECT");
                        sslStream.Close();
                        client.Close();
                    }*/
                }
                else if (String.Compare(response, "CONNECT_P") == 0)
                {
                    string sender_key = TCPCommunication.recieve_message_tcp(sslStream);
                    if (peers.ContainsKey(sender_key))
                    {
                        TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                        response = TCPCommunication.recieve_message_tcp(sslStream);
                        Console.WriteLine($"Connection request received for {response}");

                        IPEndPoint sender_ip = (IPEndPoint)client.Client.RemoteEndPoint;
                        IPEndPoint receiver_ip = peers[response];

                        if (pending_requests.ContainsKey(receiver_ip))
                        {
                            req_stream[receiver_ip] = sslStream;
                            pending_requests[receiver_ip] = false;

                            //TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                            //TCPCommunication.send_message_tcp(sslStream, pending_requests[receiver_ip].ToString());

                            /*byte[] data = new Byte[256];
                            sslStream.Read(data, 0, data.Length);
                            response = Encoding.UTF8.GetString(data);

                            PublicKeyCoordinates request_key = JsonConvert.DeserializeObject<PublicKeyCoordinates>(response);

                            data = new Byte[256];
                            data = Encoding.UTF8.GetBytes(client_keys[receiver_ip].ToString());
                            sslStream.Write(data);
                            sslStream.Flush();

                            client_keys[receiver_ip] = request_key;

                            sslStream.Close();
                            client.Close();*/


                            /*Thread.Sleep(1000);
                            pending_requests.Remove(receiver_ip);

                            sslStream.Close();
                            client.Close();*/
                        }
                        else
                        {
                            TCPCommunication.send_message_tcp(sslStream, "REJECT");
                            sslStream.Close();
                            client.Close();
                        }
                    }
                    else
                    {
                        TCPCommunication.send_message_tcp(sslStream, "REJECT");
                        sslStream.Close();
                        client.Close();
                    }

                }
                else if (String.Compare(response, "LISTEN_P") == 0)
                {

                    Byte[] data = new Byte[6];
                    sslStream.Read(data, 0, data.Length);

                    var key = KeyGenerator.GenerateDoubleBytesKey(data);
                    var hexKey = KeyGenerator.GetHexKey(key);
                    IPEndPoint listen_ip = (IPEndPoint)client.Client.RemoteEndPoint;

                    peers[hexKey] = listen_ip;

                    Console.WriteLine("Pending listener: " + listen_ip);
                    pending_requests[listen_ip] = true;

                    new Thread(() => handle_relay(listen_ip, sslStream)).Start();
                    /*string sender_key = TCPCommunication.recieve_message_tcp(sslStream);
                    if (peers.ContainsKey(sender_key))
                    {
                        //response = TCPCommunication.recieve_message_tcp(sslStream);
                        //Console.WriteLine($"Listen request received for {response}");

                        IPEndPoint listen_ip = (IPEndPoint)client.Client.RemoteEndPoint;

                        pending_requests[listen_ip] = true;

                        new Thread(() => handle_relay(listen_ip, sslStream)).Start();
                        //Thread.Sleep(1000);

                        //TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                        //TCPCommunication.send_message_tcp(sslStream, "ACCEPT");

                        //pending_requests.Add(listen_ip);
                    }
                    else
                    {
                        TCPCommunication.send_message_tcp(sslStream, "REJECT");
                        sslStream.Close();
                        client.Close();
                    }*/


                    //new Thread(() => handle_relay(relay_ip)).Start();
                }
                else
                {
                    Console.WriteLine("unrecognized command");
                }
            }
        }

        static void handle_relay(IPEndPoint listen_ip, SslStream listen_stream)
        {
            //pending_requests.Remove(client1);

            Console.WriteLine("start relay");

            while (pending_requests[listen_ip]) ;

            //SslStream request_stream = req_stream[listen_ip];

            TCPCommunication.send_message_tcp(listen_stream, "ACCEPT");

            while (listner_buffer.Count == 0) ;

            TCPCommunication.send_message_tcp(listen_stream, listner_buffer[0]);
            listner_buffer.RemoveAt(0);

            byte[] bytes = new byte[16];
            listen_stream.Read(bytes, 0, bytes.Length);

            string C = Encoding.Default.GetString(bytes);
            Console.WriteLine("C: " + C);

            var enumerator = auth_tunnel_rev.Keys.GetEnumerator();
            enumerator.MoveNext();

            IPEndPoint tunnel = enumerator.Current;

            Console.WriteLine("Sending auth message to: " + tunnel);

            //TCPCommunication.send_message_tcp(superpeer_neighbours[tunnel], "AUTH_P:" + U);
            message_buffer[0 + ":AUTH_R:" + C] = tunnel;


            while (listner_buffer.Count == 0) ;

            TCPCommunication.send_message_tcp(listen_stream, listner_buffer[0]);
            listner_buffer.RemoveAt(0);





            /*DTLSServer dtls_server0 = new DTLSServer(local_port.ToString(), new byte[] { 0xBA, 0xA0 });
            DTLSServer dtls_server1 = new DTLSServer(port.ToString(), new byte[] { 0xBA, 0xA0 });


            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                dtls_server0.Unbuffer = "winpty.exe";
                dtls_server0.Unbuffer_Args = "-Xplain -Xallow-non-tty";
                dtls_server1.Unbuffer = "winpty.exe";
                dtls_server1.Unbuffer_Args = "-Xplain -Xallow-non-tty";
            }
            else
            {
                dtls_server0.Unbuffer = "stdbuf";
                dtls_server0.Unbuffer_Args = "-i0 -o0";
                dtls_server1.Unbuffer = "stdbuf";
                dtls_server1.Unbuffer_Args = "-i0 -o0";
            }

            dtls_server0.Start();
            dtls_server1.Start();*/

            /*            TCPCommunication.send_message_tcp(listen_stream, "ACCEPT");

                        byte[] data = new Byte[256];
                        listen_stream.Read(data, 0, data.Length);
                        string response = Encoding.UTF8.GetString(data);

                        PublicKeyCoordinates listen_pub_key = JsonConvert.DeserializeObject<PublicKeyCoordinates>(response);
                        client_keys[listen_ip] = listen_pub_key;

                        Console.WriteLine("listen pubKey: " + listen_pub_key);

                        while (client_keys[listen_ip] == listen_pub_key) ;*/



            /*data = new Byte[256];
            data = Encoding.UTF8.GetBytes(client_keys[listen_ip].ToString());
            listen_stream.Write(data);
            listen_stream.Flush();

            listen_stream.Close();

            new Thread(() => dtls_server0.GetStream().CopyTo(dtls_server1.GetStream(), 16)).Start();
            new Thread(() => dtls_server1.GetStream().CopyTo(dtls_server0.GetStream(), 16)).Start();

            dtls_server0.WaitForExit();
            dtls_server1.WaitForExit();*/
        }

    }
}
