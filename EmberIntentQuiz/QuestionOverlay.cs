using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace EmberIntentQuiz;

internal sealed partial class QuestionOverlay : Control, IScreenContext
{
    private static readonly Regex ImageToken = new(@"\[\[IMAGE:([^\]]+)\]\]", RegexOptions.Compiled);
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Dictionary<string, Texture2D?> TextureCache = new(StringComparer.Ordinal);

    private readonly List<Button> _answerButtons = [];
    private QuizQuestion? _question;
    private Action<bool>? _answerCallback;
    private Action? _closedCallback;
    private Button? _skipOrContinue;
    private Label? _status;
    private Control? _firstAnswer;
    private bool _answered;
    private bool _correct;
    private bool _closed;

    public Control? DefaultFocusedControl => _firstAnswer ?? _skipOrContinue;

    public void Configure(QuizQuestion question, Action<bool> answerCallback, Action closedCallback)
    {
        _question = question;
        _answerCallback = answerCallback;
        _closedCallback = closedCallback;
        BuildUi();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (!_answered) ResolveAnswer(-1);
            CloseOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        NotifyClosed();
    }

    private void BuildUi()
    {
        if (_question is null) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var panel = new PanelContainer
        {
            AnchorLeft = 0.055f,
            AnchorTop = 0.055f,
            AnchorRight = 0.945f,
            AnchorBottom = 0.945f,
            MouseFilter = MouseFilterEnum.Stop,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color("211b16"),
            BorderColor = new Color("bf762d"),
        };
        panelStyle.SetBorderWidthAll(2);
        panelStyle.SetCornerRadiusAll(14);
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        panel.AddChild(margin);

        var layout = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", 12);
        margin.AddChild(layout);

        var title = new Label
        {
            Text = "答对题目，显露怪物意图",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        layout.AddChild(title);

        var metaText = _question.Category;
        if (!string.IsNullOrWhiteSpace(_question.Exam)) metaText += $" · {_question.Exam}";
        else if (!string.IsNullOrWhiteSpace(_question.Year)) metaText += $" · {_question.Year}";
        metaText += " · 单选（多答案题不出）";
        var meta = new Label
        {
            Text = metaText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color("d4ac79"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        meta.AddThemeFontSizeOverride("font_size", 16);
        layout.AddChild(meta);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            MouseFilter = MouseFilterEnum.Stop,
        };
        layout.AddChild(scroll);

        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);

        if (!string.IsNullOrWhiteSpace(_question.Material)
            && !Regex.IsMatch(_question.Material.Trim(), @"^[（(]?无[）)]?$"))
        {
            AddSectionTitle(content, "给定材料");
            AddTextAndImages(content, _question.Material, 17);
        }

        AddSectionTitle(content, "题目");
        AddTextAndImages(content, _question.Stem, 19);
        AddSectionTitle(content, "请选择一个答案");

        for (var index = 0; index < _question.Options.Count; index++)
        {
            var choiceIndex = index;
            var option = _question.Options[index];
            var button = new Button
            {
                Text = "",
                CustomMinimumSize = new Vector2(0, EstimateHeight(option)),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipContents = true,
                FocusMode = FocusModeEnum.All,
            };
            button.AddThemeStyleboxOverride("normal", MakeChoiceStyle(new Color("33271e"), new Color("826044")));
            button.AddThemeStyleboxOverride("hover", MakeChoiceStyle(new Color("493322"), new Color("e7a454")));
            button.AddThemeStyleboxOverride("pressed", MakeChoiceStyle(new Color("624321"), new Color("f2bd62")));
            var choiceMargin = new MarginContainer();
            choiceMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            choiceMargin.MouseFilter = MouseFilterEnum.Ignore;
            choiceMargin.AddThemeConstantOverride("margin_left", 14);
            choiceMargin.AddThemeConstantOverride("margin_right", 14);
            choiceMargin.AddThemeConstantOverride("margin_top", 10);
            choiceMargin.AddThemeConstantOverride("margin_bottom", 10);
            var choiceContent = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            choiceContent.AddThemeConstantOverride("separation", 6);
            choiceContent.AddChild(new Label
            {
                Text = $"{(char)('A' + index)}.",
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = new Color("e7b36b"),
            });
            AddTextAndImages(choiceContent, option, 17);
            choiceMargin.AddChild(choiceContent);
            button.AddChild(choiceMargin);
            button.Pressed += () => ResolveAnswer(choiceIndex);
            _answerButtons.Add(button);
            content.AddChild(button);
            _firstAnswer ??= button;
        }

        _status = new Label
        {
            Text = "每回合仅答一次。答错、按 Esc 或跳过后，本场战斗的意图保持隐藏。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.AddThemeFontSizeOverride("font_size", 16);
        layout.AddChild(_status);

        _skipOrContinue = new Button
        {
            Text = "跳过本题（意图仍隐藏）",
            CustomMinimumSize = new Vector2(0, 46),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _skipOrContinue.Pressed += () =>
        {
            if (!_answered) ResolveAnswer(-1);
            CloseOverlay();
        };
        layout.AddChild(_skipOrContinue);
    }

    private void ResolveAnswer(int selected)
    {
        if (_answered || _question is null) return;
        _answered = true;
        _correct = selected == _question.Answer;
        _answerCallback?.Invoke(_correct);
        foreach (var button in _answerButtons) button.Disabled = true;

        if (_status is not null)
        {
            var correctLetter = (char)('A' + _question.Answer);
            _status.Text = selected < 0
                ? $"已跳过。本回合意图仍隐藏；正确答案：{correctLetter}。"
                : _correct
                    ? "回答正确。本回合怪物意图已显露。"
                    : $"回答错误。本回合意图仍隐藏；正确答案：{correctLetter}。";
            _status.Modulate = new Color(_correct ? "9ddd94" : "e7a08b");
        }

        if (_skipOrContinue is not null)
        {
            _skipOrContinue.Text = "继续战斗";
            _skipOrContinue.GrabFocus();
        }
    }

    private void CloseOverlay()
    {
        if (!_answered) ResolveAnswer(-1);
        var modalContainer = MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance;
        if (modalContainer is not null && ReferenceEquals(modalContainer.OpenModal, this))
            modalContainer.Clear();
        else
            QueueFree();
        NotifyClosed();
    }

    private void NotifyClosed()
    {
        if (_closed) return;
        _closed = true;
        _closedCallback?.Invoke();
    }

    private void AddSectionTitle(VBoxContainer parent, string text)
    {
        var label = new Label { Text = text, Modulate = new Color("e7b36b") };
        label.AddThemeFontSizeOverride("font_size", 18);
        parent.AddChild(label);
    }

    private void AddTextAndImages(VBoxContainer parent, string text, int fontSize)
    {
        var matches = ImageToken.Matches(text);
        var cursor = 0;
        foreach (Match match in matches)
        {
            AddTextBlock(parent, text[cursor..match.Index], fontSize);
            AddQuestionImage(parent, match.Groups[1].Value);
            cursor = match.Index + match.Length;
        }
        AddTextBlock(parent, text[cursor..], fontSize);
    }

    private void AddTextBlock(VBoxContainer parent, string markdown, int fontSize)
    {
        var text = CleanMarkdown(markdown);
        if (string.IsNullOrWhiteSpace(text)) return;
        var richText = new RichTextLabel
        {
            Text = text,
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 26),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        richText.AddThemeFontSizeOverride("normal_font_size", fontSize);
        parent.AddChild(richText);
    }

    private void AddQuestionImage(VBoxContainer parent, string relativePath)
    {
        var texture = LoadQuestionImage(relativePath);
        if (texture is null)
        {
            parent.AddChild(new Label
            {
                Text = "[原题配图无法读取]",
                Modulate = new Color("e7a08b"),
                MouseFilter = MouseFilterEnum.Ignore,
            });
            return;
        }

        var width = Math.Min(760, texture.GetWidth());
        var height = Math.Min(420, width * texture.GetHeight() / Math.Max(1, texture.GetWidth()));
        if (height < 1) height = 1;
        if (height == 420) width = height * texture.GetWidth() / Math.Max(1, texture.GetHeight());
        parent.AddChild(new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(width, height),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    private static Texture2D? LoadQuestionImage(string relativePath)
    {
        if (TextureCache.TryGetValue(relativePath, out var cached)) return cached;
        try
        {
            if (TextureCache.Count >= 64) TextureCache.Remove(TextureCache.Keys.First());
            var modRoot = Path.GetFullPath(ModEntry.ModDirectory);
            var file = Path.GetFullPath(Path.Combine(modRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(modRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Question image path escaped the mod folder.");
            if (!File.Exists(file)) throw new FileNotFoundException("Question image is missing.", file);
            var image = Image.LoadFromFile(file);
            if (image.IsEmpty()) throw new InvalidDataException("Question image could not be decoded.");
            var texture = ImageTexture.CreateFromImage(image);
            TextureCache[relativePath] = texture;
            return texture;
        }
        catch
        {
            TextureCache[relativePath] = null;
            return null;
        }
    }

    private static StyleBoxFlat MakeChoiceStyle(Color background, Color border)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);
        return style;
    }

    private static int EstimateHeight(string option)
    {
        var textLength = Regex.Replace(option, @"\[\[IMAGE:[^\]]+\]\]", "").Length;
        var lines = Math.Max(1, (int)Math.Ceiling(textLength / 70.0));
        var imageHeight = 0;
        foreach (Match image in ImageToken.Matches(option))
        {
            var texture = LoadQuestionImage(image.Groups[1].Value);
            if (texture is null) continue;
            var width = Math.Min(760, texture.GetWidth());
            var height = Math.Min(420, width * texture.GetHeight() / Math.Max(1, texture.GetWidth()));
            imageHeight += Math.Max(1, height) + 8;
        }
        return 78 + lines * 28 + imageHeight;
    }

    private static string CleanMarkdown(string value)
    {
        var text = MarkdownLink.Replace(value, "$1");
        text = Regex.Replace(text, @"(?m)^\s{0,3}#{1,6}\s*", "");
        text = Regex.Replace(text, @"(?m)^\s*[-*+]\s+", "• ");
        text = Regex.Replace(text, @"(?m)^\s*\d+[.)、]\s*", "• ");
        text = text.Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
        return text.Trim();
    }
}
