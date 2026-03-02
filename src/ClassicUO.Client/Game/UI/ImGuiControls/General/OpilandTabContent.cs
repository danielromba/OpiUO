using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class OpilandTabContent : TabContent
    {
        #region Properties

        private bool ClientAutostart;
        private bool ServerAutostart;
        private bool ServerRunning;
        private bool ClientConnected;
        private string ServerIp;
        private string ServerPort;
        private string ServerPassword;
        private string ClientIp;
        private string ClientPort;
        private string ClientPassword;
        private string _statusMessage = "Server stopped";
        private string _clientStatusMessage = "Not connected";
        private int _connectedClients;
        private string _messageToSend = "";
        private string CustomName = "";
        private int CustomNameHue = 0x005A;

        #endregion

        public OpilandTabContent()
        {
            ClientAutostart = ProfileManager.CurrentProfile.OpilandClientAutostart;
            ServerAutostart = ProfileManager.CurrentProfile.OpilandServerAutostart;
            ServerIp = ProfileManager.CurrentProfile.OpilandServerIp;
            ServerPort = ProfileManager.CurrentProfile.OpilandServerPort;
            ServerPassword = ProfileManager.CurrentProfile.OpilandServerPassword;
            CustomName = ProfileManager.CurrentProfile.OpilandClientCustomName;
            CustomNameHue = ProfileManager.CurrentProfile.OpilandClientCustomNameHue;

            // Load client settings (reusing same fields for now, can be separated if needed)
            ClientIp = ServerIp;
            ClientPort = ServerPort;
            ClientPassword = ServerPassword;

            // Subscribe to server events
            OpilandServerManager.Instance.StateChanged += OnServerStateChanged;
            OpilandServerManager.Instance.ClientConnected += OnClientConnected;
            OpilandServerManager.Instance.ClientDisconnected += OnClientDisconnected;

            // Subscribe to client events
            OpilandClientManager.Instance.StateChanged += OnClientStateChanged;
            OpilandClientManager.Instance.MessageReceived += OnClientMessageReceived;

            // Get initial state
            ServerRunning = OpilandServerManager.Instance.IsRunning;
            ClientConnected = OpilandClientManager.Instance.IsConnected;
            _connectedClients = OpilandServerManager.Instance.ConnectedClientsCount;
        }

        public void Cleanup()
        {
            // Unsubscribe from server events
            OpilandServerManager.Instance.StateChanged -= OnServerStateChanged;
            OpilandServerManager.Instance.ClientConnected -= OnClientConnected;
            OpilandServerManager.Instance.ClientDisconnected -= OnClientDisconnected;

            // Unsubscribe from client events
            OpilandClientManager.Instance.StateChanged -= OnClientStateChanged;
            OpilandClientManager.Instance.MessageReceived -= OnClientMessageReceived;
        }

        private void OnServerStateChanged(object sender, ServerStateChangedEventArgs e)
        {
            ServerRunning = e.IsRunning;
            _statusMessage = e.Message;
        }

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        {
            _connectedClients = OpilandServerManager.Instance.ConnectedClientsCount;
            GameActions.Print(World.Instance, $"Opiland client connected: {e.Address}");
        }

        private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            _connectedClients = OpilandServerManager.Instance.ConnectedClientsCount;
            GameActions.Print(World.Instance, "Opiland client disconnected");
        }

        private void OnClientStateChanged(object sender, ClientStateChangedEventArgs e)
        {
            ClientConnected = e.IsConnected;
            _clientStatusMessage = e.Message;
        }

        private void OnClientMessageReceived(object sender, ClientMessageReceivedEventArgs e)
        {
            GameActions.Print(World.Instance, $"Opiland: {e.Message}");
        }

        public override void DrawContent()
        {
            Profile currentProfile = ProfileManager.CurrentProfile;

            ImGui.Spacing();

            if (ImGui.BeginTabBar("##Opiland"))
            {
                if (ImGui.BeginTabItem("Client"))
                {
                    DrawClientTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Server"))
                {
                    DrawServerTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawClientTab()
        {
            if (ImGui.BeginTable("##left", 2))
            {
                ImGui.TableSetupColumn("left", ImGuiTableColumnFlags.IndentDisable | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("right", ImGuiTableColumnFlags.IndentDisable | ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                // Use a table for consistent two-column layout
                if (ImGui.BeginTable("##ClientSettingsLeft", 2, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthStretch);

                    // Address
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Address");
                    ImGuiComponents.Tooltip("Server address to connect to");

                    ImGui.TableSetColumnIndex(1);
                    ImGui.SetNextItemWidth(120);

                    // Disable input when client is connected
                    if (ClientConnected) ImGui.BeginDisabled();
                    ImGui.InputText("##ClientAddress", ref ClientIp, 15);
                    if (ClientConnected) ImGui.EndDisabled();

                    // Port
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Port");
                    ImGuiComponents.Tooltip("Server port to connect to");

                    ImGui.TableSetColumnIndex(1);
                    ImGui.SetNextItemWidth(60);

                    if (ClientConnected) ImGui.BeginDisabled();
                    ImGui.InputText("##ClientPort", ref ClientPort, 6);
                    if (ClientConnected) ImGui.EndDisabled();

                    // Password
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Password");
                    ImGuiComponents.Tooltip("Optional server password");

                    ImGui.TableSetColumnIndex(1);
                    ImGui.SetNextItemWidth(120);
                    ImGui.InputText("##ClientPassword", ref ClientPassword, 100, ImGuiInputTextFlags.Password);

                    // Autostart
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Autostart");

                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.Checkbox("##ClientAutostart", ref ClientAutostart))
                    {
                        ProfileManager.CurrentProfile.OpilandClientAutostart = ClientAutostart;
                    }

                    // Status
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Status");

                    ImGui.TableSetColumnIndex(1);
                    if (ClientConnected)
                        ImGui.TextColored(new Vector4(0, 0.8f, 0, 1f), "Connected");
                    else
                        ImGui.TextColored(new Vector4(0.8f, 0, 0, 1f), "Not connected");

                    // Connect/Disconnect button
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();

                    // Colored button
                    Vector4 buttonColor = ClientConnected
                        ? new Vector4(0.6f, 0.2f, 0.2f, 1.0f)   // Red for Disconnect
                        : new Vector4(0.2f, 0.5f, 0.2f, 1.0f);  // Green for Connect

                    ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { X = buttonColor.X * 1.2f, Y = buttonColor.Y * 1.2f, Z = buttonColor.Z * 1.2f });
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor with { X = buttonColor.X * 0.8f, Y = buttonColor.Y * 0.8f, Z = buttonColor.Z * 0.8f });

                    if (ImGui.Button(ClientConnected ? "Disconnect" : "Connect"))
                    {
                        ConnectDisconnectClient();
                    }

                    ImGui.PopStyleColor(3);

                    ImGui.EndTable();
                }

                ImGui.TableSetColumnIndex(1);

                // Use a table for consistent two-column layout
                if (ImGui.BeginTable("##ClientSettingsRight", 2, ImGuiTableFlags.None))
                {
                    ImGui.TableSetupColumn("Label2", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Input2", ImGuiTableColumnFlags.WidthStretch);

                    // Name
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Name");
                    ImGuiComponents.Tooltip("Choose custom name for Opiland. Player name is used when empty.");

                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.InputText("##Name", ref CustomName, 20))
                    {
                        ProfileManager.CurrentProfile.OpilandClientCustomName = CustomName;
                    }

                    // Hue
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Hue");
                    ImGuiComponents.Tooltip("Choose custom name hue for Opiland. You can target items only.");

                    ImGui.TableSetColumnIndex(1);
                    if (ImGui.Button($"{CustomNameHue:X4}"))
                    {
                        World.Instance.TargetManager.SetTargeting((targetedItem) =>
                        {
                            if (targetedItem != null && targetedItem is Entity targetedEntity)
                            {
                                if (SerialHelper.IsItem(targetedEntity))
                                {
                                    CustomNameHue = ProfileManager.CurrentProfile.OpilandClientCustomNameHue = targetedEntity.Hue;
                                }
                            }
                        });
                    }

                    ImGui.EndTable();
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();

            // Message sending section (only when connected)
            if (ClientConnected)
            {
                ImGui.SeparatorText("Send Message:");

                ImGui.SetNextItemWidth(300);
                if (ImGui.InputText("##MessageInput", ref _messageToSend, 500, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    if (!string.IsNullOrWhiteSpace(_messageToSend))
                    {
                        OpilandClientManager.Instance.SendMessage(_messageToSend);
                        GameActions.Print(World.Instance, $"Sent: {_messageToSend}");
                        _messageToSend = ""; // Clear after sending
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Send"))
                {
                    if (!string.IsNullOrWhiteSpace(_messageToSend))
                    {
                        OpilandClientManager.Instance.SendMessage(_messageToSend);
                        GameActions.Print(World.Instance, $"Sent: {_messageToSend}");
                        _messageToSend = ""; // Clear after sending
                    }
                }

                ImGui.Spacing();
            }

            ImGui.SeparatorText("Status:");
            ImGui.TextWrapped(_clientStatusMessage);
        }

        private void DrawServerTab()
        {
            // Use a table for consistent two-column layout
            if (ImGui.BeginTable("Settings", 2, ImGuiTableFlags.None))
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthStretch);

                // Address
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Address");
                ImGuiComponents.Tooltip("Opiland's server address. Use public address if you want players to connect over the internet.");

                ImGui.TableSetColumnIndex(1);
                ImGui.SetNextItemWidth(120);

                // Disable input when server is running
                if (ServerRunning) ImGui.BeginDisabled();
                ImGui.InputText("##Address", ref ServerIp, 15);
                if (ServerRunning) ImGui.EndDisabled();

                // Port
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Port");
                ImGuiComponents.Tooltip("Opiland's server port.");

                ImGui.TableSetColumnIndex(1);
                ImGui.SetNextItemWidth(60);

                if (ServerRunning) ImGui.BeginDisabled();
                ImGui.InputText("##Port", ref ServerPort, 6);
                if (ServerRunning) ImGui.EndDisabled();

                // Password
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Password");
                ImGuiComponents.Tooltip("Opiland's server password. Can be empty.");

                ImGui.TableSetColumnIndex(1);
                ImGui.SetNextItemWidth(120);
                ImGui.InputText("##Password", ref ServerPassword, 100);

                // Status
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Status");

                ImGui.TableSetColumnIndex(1);
                if (ServerRunning)
                    ImGui.TextColored(new Vector4(0, 0.8f, 0, 1f), $"Running ({_connectedClients} clients)");
                else
                    ImGui.TextColored(new Vector4(0.8f, 0, 0, 1f), "Stopped");

                // Autostart
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Autostart");

                ImGui.TableSetColumnIndex(1);
                if (ImGui.Checkbox("##ClientAutostart", ref ServerAutostart))
                {
                    ProfileManager.CurrentProfile.OpilandServerAutostart = ServerAutostart;
                }

                // Start/Stop button
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();

                // Colored button
                Vector4 buttonColor = ServerRunning
                    ? new Vector4(0.6f, 0.2f, 0.2f, 1.0f)   // Red for Stop
                    : new Vector4(0.2f, 0.5f, 0.2f, 1.0f);  // Green for Start

                ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { X = buttonColor.X * 1.2f, Y = buttonColor.Y * 1.2f, Z = buttonColor.Z * 1.2f });
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor with { X = buttonColor.X * 0.8f, Y = buttonColor.Y * 0.8f, Z = buttonColor.Z * 0.8f });

                if (ImGui.Button(ServerRunning ? "Stop Server" : "Start Server"))
                {
                    StartStopServer();
                }

                ImGui.PopStyleColor(3);

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Status:");
            ImGui.TextWrapped(_statusMessage);
        }

        private void ConnectDisconnectClient()
        {
            if (ClientConnected)
            {
                OpilandClientManager.Instance.Disconnect();
            }
            else
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(ClientIp))
                {
                    _clientStatusMessage = "Error: Address cannot be empty";
                    return;
                }

                if (!int.TryParse(ClientPort, out int port) || port < 1 || port > 65535)
                {
                    _clientStatusMessage = "Error: Port must be between 1 and 65535";
                    return;
                }

                // Connect (async operation)
                _ = OpilandClientManager.Instance.ConnectAsync(ClientIp, port, ClientPassword);
            }
        }

        private void StartStopServer()
        {
            if (ServerRunning)
            {
                OpilandServerManager.Instance.StopServer();
            }
            else
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(ServerIp))
                {
                    _statusMessage = "Error: Address cannot be empty";
                    return;
                }

                if (!int.TryParse(ServerPort, out int port) || port < 1 || port > 65535)
                {
                    _statusMessage = "Error: Port must be between 1 and 65535";
                    return;
                }

                // Save settings
                ProfileManager.CurrentProfile.OpilandServerIp = ServerIp;
                ProfileManager.CurrentProfile.OpilandServerPort = ServerPort;
                ProfileManager.CurrentProfile.OpilandServerPassword = ServerPassword;

                // Start server
                OpilandServerManager.Instance.StartServer(ServerIp, port);
            }
        }
    }
}
