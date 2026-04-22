using System;
using ShadowLink.Localization;
using ShadowLink.Core.Models;

namespace ShadowLink.Application.ViewModels;

public sealed class ShortcutBindingViewModel : ObservableObject
{
    private String _name;
    private String _gesture;
    private String _description;
    private Boolean _isEnabled;
    private readonly String _nameKey;
    private readonly String _descriptionKey;

    public ShortcutBindingViewModel(ShortcutBinding binding)
    {
        _nameKey = binding.Name;
        _descriptionKey = binding.Description;
        _name = ShadowLinkText.TranslateOrOriginal(binding.Name);
        _gesture = binding.Gesture;
        _description = ShadowLinkText.TranslateOrOriginal(binding.Description);
        _isEnabled = binding.IsEnabled;
    }

    public String NameKey => _nameKey;

    public String Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public String Gesture
    {
        get => _gesture;
        set => SetProperty(ref _gesture, value);
    }

    public String Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public Boolean IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public ShortcutBinding ToModel()
    {
        return new ShortcutBinding
        {
            Name = _nameKey,
            Gesture = Gesture.Trim(),
            Description = _descriptionKey,
            IsEnabled = IsEnabled
        };
    }
}
