using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LanBubble;

internal sealed class MainForm : Form
{
    private const int BroadcastPort = 50000;
    private const int MaxMessageLength = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly (string Name, Color Color)[] Tones =
    [
        ("Info", Color.FromArgb(52, 126, 245)),
        ("Success", Color.FromArgb(22, 163, 74)),
        ("Warning", Color.FromArgb(234, 168, 19)),
        ("Alert", Color.FromArgb(220, 38, 38)),
        ("Dark", Color.FromArgb(31, 41, 55)),
        ("Brand", Color.FromArgb(108, 92, 231))
    ];

    private readonly UdpClient _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TextBox _messageBox;
    private readonly TextBox _nameBox;
    private readonly ComboBox _positionBox;
    private readonly TrackBar _sizeSlider;
    private readonly Label _sizeValue;
    private readonly Label _statusLabel;
    private readonly FlowLayoutPanel _history;
    private readonly List<Button> _toneButtons = [];
    private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private Color _selectedTone = Tones[0].Color;
    private string _selectedToneName = Tones[0].Name;
    private bool _closing;

    public MainForm()
    {
        Text = "LAN Bubble";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 600);
        Size = new Size(900, 720);
        BackColor = Color.FromArgb(248, 249, 251);
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);

        var tonePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BackColor,
            Padding = new Padding(0, 3, 0, 0)
        };
        foreach (var tone in Tones)
        {
            var button = new Button
            {
                Text = $"●  {tone.Name}",
                Tag = tone,
                AutoSize = true,
                Height = 34,
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(10, 0, 10, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(35, 40, 48),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(213, 216, 222);
            button.FlatAppearance.BorderSize = 1;
            button.Click += (_, _) => SelectTone(button, tone.Name, tone.Color);
            _toneButtons.Add(button);
            tonePanel.Controls.Add(button);
        }
        root.Controls.Add(tonePanel, 0, 0);

        _messageBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12),
            MaxLength = MaxMessageLength,
            PlaceholderText = "Type a message to broadcast on your LAN…",
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 4)
        };
        root.Controls.Add(_messageBox, 0, 1);

        var namePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BackColor,
            Margin = Padding.Empty
        };
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        namePanel.Controls.Add(new Label
        {
            Text = "Name",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(86, 92, 103)
        }, 0, 0);
        _nameBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = Environment.UserName,
            MaxLength = 40,
            BorderStyle = BorderStyle.FixedSingle
        };
        namePanel.Controls.Add(_nameBox, 1, 0);
        root.Controls.Add(namePanel, 0, 2);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = BackColor,
            Padding = new Padding(0, 3, 0, 0)
        };
        options.Controls.Add(new Label
        {
            Text = "Position",
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            ForeColor = Color.FromArgb(86, 92, 103)
        });
        _positionBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            Height = 32,
            Margin = new Padding(0, 1, 18, 0)
        };
        _positionBox.Items.AddRange(["Bottom right", "Bottom left", "Top right", "Top left"]);
        _positionBox.SelectedIndex = 0;
        options.Controls.Add(_positionBox);
        options.Controls.Add(new Label
        {
            Text = "Size",
            AutoSize = true,
            Margin = new Padding(0, 8, 6, 0),
            ForeColor = Color.FromArgb(86, 92, 103)
        });
        _sizeSlider = new TrackBar
        {
            Minimum = 1,
            Maximum = 3,
            Value = 2,
            TickStyle = TickStyle.None,
            Width = 105,
            Height = 34,
            Margin = new Padding(0, 0, 2, 0)
        };
        _sizeSlider.ValueChanged += (_, _) => _sizeValue.Text = ["Small", "Medium", "Large"][_sizeSlider.Value - 1];
        options.Controls.Add(_sizeSlider);
        _sizeValue = new Label
        {
            Text = "Medium",
            AutoSize = true,
            Margin = new Padding(0, 8, 14, 0),
            ForeColor = Color.FromArgb(86, 92, 103)
        };
        options.Controls.Add(_sizeValue);
        var sendButton = new Button
        {
            Text = "Send bubble ↗",
            AutoSize = true,
            Height = 36,
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 0)
        };
        sendButton.FlatAppearance.BorderColor = Color.FromArgb(190, 195, 204);
        sendButton.Click += async (_, _) => await SendBubbleAsync();
        options.Controls.Add(sendButton);
        root.Controls.Add(options, 0, 3);

        var historyFrame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0, 3, 0, 0)
        };
        historyFrame.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(224, 226, 231));
            e.Graphics.DrawRectangle(pen, 0, 0, historyFrame.Width - 1, historyFrame.Height - 1);
        };
        _history = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(4)
        };
        historyFrame.Controls.Add(_history);
        root.Controls.Add(historyFrame, 0, 4);

        _statusLabel = new Label
        {
            Text = $"Ready · listening on UDP {BroadcastPort}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(98, 105, 117),
            Margin = new Padding(2, 3, 0, 0)
        };
        root.Controls.Add(_statusLabel, 0, 5);

        SelectTone(_toneButtons[0], Tones[0].Name, Tones[0].Color);
        AddHistoryNote("Run LAN Bubble on each PC that should receive messages. Choose a style and send a short bubble.");

        _listener = new UdpClient(new IPEndPoint(IPAddress.Any, BroadcastPort));
        _listener.Client.EnableBroadcast = true;
        _ = ReceiveLoopAsync();
    }

    private void SelectTone(Button selected, string name, Color color)
    {
        _selectedTone = color;
        _selectedToneName = name;
        foreach (var button in _toneButtons)
        {
            var isSelected = ReferenceEquals(button, selected);
            button.BackColor = isSelected ? Color.FromArgb(237, 244, 255) : Color.White;
            button.FlatAppearance.BorderColor = isSelected ? color : Color.FromArgb(213, 216, 222);
        }
    }

    private async Task SendBubbleAsync()
    {
        var message = _messageBox.Text.Trim();
        if (message.Length == 0)
        {
            _statusLabel.Text = "Enter a message first.";
            _messageBox.Focus();
            return;
        }

        var packet = new BubblePacket
        {
            Id = Guid.NewGuid().ToString("N"),
            Sender = string.IsNullOrWhiteSpace(_nameBox.Text) ? Environment.UserName : _nameBox.Text.Trim(),
            Message = message,
            Tone = _selectedToneName,
            Position = _positionBox.SelectedItem?.ToString() ?? "Bottom right",
            Size = _sizeSlider.Value,
            SentAtUtc = DateTime.UtcNow
        };

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, JsonOptions);
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            sender.EnableBroadcast = true;
            await sender.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
            Remember(packet.Id);
            ShowBubble(packet);
            AddHistoryBubble(packet, false);
            _messageBox.Clear();
            _statusLabel.Text = $"Sent to the local network · UDP {BroadcastPort}";
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            _statusLabel.Text = $"Could not send: {ex.Message}";
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var result = await _listener.ReceiveAsync(_shutdown.Token);
                if (result.Buffer.Length == 0 || result.Buffer.Length > 1400)
                    continue;

                BubblePacket? packet;
                try
                {
                    packet = JsonSerializer.Deserialize<BubblePacket>(result.Buffer, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (!IsValid(packet) || !Remember(packet!.Id))
                    continue;
                BeginInvoke(() =>
                {
                    if (_closing) return;
                    ShowBubble(packet);
                    AddHistoryBubble(packet, true);
                    _statusLabel.Text = $"Received from {packet.Sender} · {result.RemoteEndPoint.Address}";
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_shutdown.IsCancellationRequested) { }
        catch (InvalidOperationException) when (_closing) { }
    }

    private static bool IsValid(BubblePacket? packet) =>
        packet is not null &&
        packet.Type == "bubble" &&
        !string.IsNullOrWhiteSpace(packet.Id) && packet.Id.Length <= 40 &&
        !string.IsNullOrWhiteSpace(packet.Message) && packet.Message.Length <= MaxMessageLength &&
        (packet.Size is >= 1 and <= 3) &&
        Tones.Any(t => t.Name.Equals(packet.Tone, StringComparison.OrdinalIgnoreCase)) &&
        new[] { "Bottom right", "Bottom left", "Top right", "Top left" }.Contains(packet.Position);

    private bool Remember(string id)
    {
        lock (_seenIds)
        {
            if (!_seenIds.Add(id)) return false;
            _seenOrder.Enqueue(id);
            while (_seenOrder.Count > 500)
                _seenIds.Remove(_seenOrder.Dequeue());
            return true;
        }
    }

    private void ShowBubble(BubblePacket packet)
    {
        var tone = Tones.First(t => t.Name.Equals(packet.Tone, StringComparison.OrdinalIgnoreCase)).Color;
        var toast = new BubbleToast(packet.Sender, packet.Message, tone, packet.Position, packet.Size);
        toast.Show();
    }

    private void AddHistoryBubble(BubblePacket packet, bool incoming)
    {
        var tone = Tones.First(t => t.Name.Equals(packet.Tone, StringComparison.OrdinalIgnoreCase)).Color;
        var card = new Panel
        {
            Width = Math.Max(450, _history.ClientSize.Width - 35),
            Height = packet.Size == 1 ? 72 : packet.Size == 2 ? 88 : 108,
            BackColor = Color.White,
            Margin = new Padding(4, 4, 4, 8),
            Padding = new Padding(13)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(224, 226, 231));
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            using var brush = new SolidBrush(tone);
            e.Graphics.FillEllipse(brush, 14, 17, 10, 10);
        };
        var title = new Label
        {
            Text = packet.Message,
            Location = new Point(32, 12),
            Size = new Size(card.Width - 52, card.Height - 38),
            AutoEllipsis = true,
            Font = new Font("Segoe UI", packet.Size == 3 ? 13 : 11),
            ForeColor = Color.FromArgb(38, 42, 49)
        };
        var byline = new Label
        {
            Text = $"{packet.Sender} · {packet.SentAtUtc.ToLocalTime():t} · {(incoming ? "received" : "sent")}",
            Location = new Point(32, card.Height - 25),
            AutoSize = true,
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.FromArgb(120, 126, 136)
        };
        card.Controls.Add(title);
        card.Controls.Add(byline);
        _history.Controls.Add(card);
        _history.ScrollControlIntoView(card);
        while (_history.Controls.Count > 50)
        {
            var oldest = _history.Controls[0];
            _history.Controls.RemoveAt(0);
            oldest.Dispose();
        }
    }

    private void AddHistoryNote(string text)
    {
        _history.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Width = 620,
            Height = 48,
            ForeColor = Color.FromArgb(120, 126, 136),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8)
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _shutdown.Cancel();
        _listener.Close();
        _shutdown.Dispose();
        base.OnFormClosing(e);
    }
}

internal sealed class BubblePacket
{
    public string Type { get; set; } = "bubble";
    public string Id { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Message { get; set; } = "";
    public string Tone { get; set; } = "Info";
    public string Position { get; set; } = "Bottom right";
    public int Size { get; set; } = 2;
    public DateTime SentAtUtc { get; set; }
}

internal sealed class BubbleToast : Form
{
    private readonly System.Windows.Forms.Timer _timer;

    public BubbleToast(string sender, string message, Color tone, string position, int size)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Padding = new Padding(14);
        Size = size == 1 ? new Size(330, 86) : size == 2 ? new Size(390, 105) : new Size(470, 130);

        var edge = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = tone };
        var name = new Label
        {
            Text = sender,
            Dock = DockStyle.Top,
            Height = 23,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = tone,
            Padding = new Padding(4, 0, 0, 0)
        };
        var text = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", size == 3 ? 13 : 11),
            ForeColor = Color.FromArgb(35, 39, 45),
            Padding = new Padding(4, 2, 4, 0),
            AutoEllipsis = true
        };
        Controls.Add(text);
        Controls.Add(name);
        Controls.Add(edge);
        Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 223, 229));
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
        Click += (_, _) => Close();
        foreach (Control control in Controls) control.Click += (_, _) => Close();

        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = 18;
        var x = position.EndsWith("left", StringComparison.OrdinalIgnoreCase)
            ? area.Left + margin
            : area.Right - Width - margin;
        var y = position.StartsWith("top", StringComparison.OrdinalIgnoreCase)
            ? area.Top + margin
            : area.Bottom - Height - margin;
        Location = new Point(x, y);

        _timer = new System.Windows.Forms.Timer { Interval = 7000 };
        _timer.Tick += (_, _) => Close();
        FormClosed += (_, _) => _timer.Dispose();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }
}
