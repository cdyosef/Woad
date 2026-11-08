using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Speech: what's said shows in a bubble over the speaker, in their language's script, turning to English for those
/// who understand it. The heroes listening are the players' (not the speaker): the bubble shows what the best of them
/// makes of it. Press Enter on your turn to say something yourself: pick one of your languages and type.
/// </summary>
public partial class GameScreen
{
    private Panel? _chat;

    /// <summary>The speech bubble over each speaker (the last thing they said).</summary>
    private readonly Dictionary<int, SpeechBubble> _bubbles = [];

    /// <summary>For the click tests: whether a speech bubble shows over this speaker.</summary>
    internal bool HasBubble(int speakerId) => _bubbles.TryGetValue(speakerId, out SpeechBubble? bubble) && IsInstanceValid(bubble);
    private OptionButton _chatLanguage = null!;
    private LineEdit _chatText = null!;

    /// <summary>Whether the chat box is open: the hotkeys wait while it is.</summary>
    internal bool Chatting => _chat?.Visible == true;

    /// <summary>What the players' heroes listening (all but the speaker) make of something said in <paramref name="language"/>.</summary>
    private Understanding Heard(Language language, int speakerId) =>
        Languages.Best(language, _session.State.Heroes
            .Where(hero => !hero.IsCompanion && hero.IsInPlay && hero.Id != speakerId)
            .Select(hero => hero.Appearance.Speaks));

    /// <summary>Puts words in a bubble over whoever said them (if they're on the board), and in the log.</summary>
    private void Say(int speakerId, string text, Language language)
    {
        Understanding understanding = Heard(language, speakerId);
        if (_tokens.TryGetValue(speakerId, out TokenView? token))
        {
            if (_bubbles.Remove(speakerId, out SpeechBubble? old))
            {
                old.QueueFree();
            }

            // On the board, following the speaker's figure (figures are made afresh as rooms are shown).
            var bubble = new SpeechBubble
            {
                Head = new Vector2(BoardView.TileSize / 2f, 2),
                Position = token.Position,
                Follow = () => _tokens.TryGetValue(speakerId, out TokenView? figure) ? figure.Position : null,
            };
            _board.AddChild(bubble);
            _bubbles[speakerId] = bubble;
            bubble.Say(text, language, understanding);
        }

        string heard = understanding is Understanding.Fully or Understanding.Slowly ? text
            : understanding == Understanding.Some ? string.Join(" ", text.Split(' ').Select(word => Languages.MakesOut(language, understanding, word) ? word : "~~~"))
            : "~~~";
        Log(language == Languages.Common ? $"{NameOf(speakerId)}: \"{text}\"" : $"{NameOf(speakerId)}, in {language.Name}: \"{heard}\"", Palette.TrimBright);
    }

    /// <summary>Opens the chat box for the hero whose turn it is: their languages to pick from, and a line to type.</summary>
    private void OpenChat()
    {
        if (ActiveHero is not { IsCompanion: false } hero || _autoplay)
        {
            return;
        }

        if (_chat is null)
        {
            _chat = new Panel { Position = new Vector2(180, 300), Size = new Vector2(282, 24), Visible = false };
            _chatLanguage = new OptionButton { Position = new Vector2(3, 3), Size = new Vector2(76, 18) };
            _chatText = new LineEdit { Position = new Vector2(82, 3), Size = new Vector2(196, 18), PlaceholderText = "Say something (Enter)", MaxLength = 72 };
            _chatText.TextSubmitted += SendChat;
            _chatText.GuiInput += @event =>
            {
                if (@event.IsActionPressed("ui_cancel"))
                {
                    CloseChat();
                    _chatText.AcceptEvent();
                }
            };
            _chat.AddChild(_chatLanguage);
            _chat.AddChild(_chatText);
            AddChild(_chat);
        }

        _chatLanguage.Clear();
        foreach (Language language in hero.Appearance.Speaks)
        {
            _chatLanguage.AddItem(language.Name);
        }

        _chatText.Text = "";
        _chat.Visible = true;
        _chatText.GrabFocus();
    }

    private void SendChat(string text)
    {
        string said = text.Trim();
        if (said.Length > 0 && ActiveHero is { } hero)
        {
            IReadOnlyList<Language> speaks = hero.Appearance.Speaks;
            Language language = speaks[Math.Clamp(_chatLanguage.Selected, 0, speaks.Count - 1)];
            Say(hero.Id, said, language);
        }

        CloseChat();
    }

    private void CloseChat()
    {
        if (_chat is not null)
        {
            _chat.Visible = false;
            _chatText.ReleaseFocus();
        }
    }
}
