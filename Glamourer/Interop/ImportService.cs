﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.Internal.Notifications;
using Glamourer.Customization;
using Glamourer.Designs;
using Glamourer.Services;
using ImGuiNET;
using OtterGui.Classes;

namespace Glamourer.Interop;

public class ImportService(CustomizationService _customizations, IDragDropManager _dragDropManager, ItemManager _items)
{
    public void CreateDatSource()
        => _dragDropManager.CreateImGuiSource("DatDragger", m => m.Files.Count == 1 && m.Extensions.Contains(".dat"), m =>
        {
            ImGui.TextUnformatted($"Dragging {Path.GetFileName(m.Files[0])} to import customizations for Glamourer...");
            return true;
        });

    public void CreateCharaSource()
        => _dragDropManager.CreateImGuiSource("CharaDragger", m => m.Files.Count == 1 && m.Extensions.Contains(".chara"), m =>
        {
            ImGui.TextUnformatted($"Dragging {Path.GetFileName(m.Files[0])} to import Anamnesis data for Glamourer...");
            return true;
        });

    public bool CreateDatTarget(out DatCharacterFile file)
    {
        if (!_dragDropManager.CreateImGuiTarget("DatDragger", out var files, out _) || files.Count != 1)
        {
            file = default;
            return false;
        }

        return LoadDat(files[0], out file);
    }

    public bool CreateCharaTarget([NotNullWhen(true)] out DesignBase? design, out string name)
    {
        if (!_dragDropManager.CreateImGuiTarget("CharaDragger", out var files, out _) || files.Count != 1)
        {
            design = null;
            name   = string.Empty;
            return false;
        }

        return LoadChara(files[0], out design, out name);
    }

    public bool LoadChara(string path, [NotNullWhen(true)] out DesignBase? design, out string name)
    {
        if (!File.Exists(path))
        {
            design = null;
            name   = string.Empty;
            return false;
        }

        try
        {
            var text = File.ReadAllText(path);
            var file = CharaFile.CharaFile.ParseData(_items, text, Path.GetFileNameWithoutExtension(path));
            if (file == null)
                throw new Exception();

            name   = file.Name;
            design = new DesignBase(_customizations, file.Data, file.ApplyEquip, file.ApplyCustomize);
        }
        catch (Exception ex)
        {
            Glamourer.Messager.NotificationMessage(ex, $"Could not read .chara file {path}.", NotificationType.Error);
            design = null;
            name   = string.Empty;
            return false;
        }

        return true;
    }

    public bool LoadDat(string path, out DatCharacterFile file)
    {
        if (!File.Exists(path))
        {
            file = default;
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            if (!DatCharacterFile.Read(stream, out file))
                return false;

            if (!Verify(file))
                return false;
        }
        catch (Exception ex)
        {
            Glamourer.Messager.NotificationMessage(ex, $"Could not read character data file {path}.", NotificationType.Error);
            file = default;
        }

        return true;
    }

    public bool SaveDesignAsDat(string path, in Customize input, string description)
    {
        if (!Verify(input, out var voice))
            return false;

        if (description.Length > 40)
            return false;

        if (path.Length == 0)
            return false;

        try
        {
            var file        = new DatCharacterFile(input, voice, description);
            var directories = Path.GetDirectoryName(path);
            if (directories != null)
                Directory.CreateDirectory(directories);
            using var stream = File.Open(path, File.Exists(path) ? FileMode.Truncate : FileMode.CreateNew);
            file.Write(stream);

            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Messager.NotificationMessage(ex, $"Could not save character data to file {path}.", "Failure", NotificationType.Error);
            return false;
        }
    }

    public bool Verify(in Customize input, out byte voice, byte? inputVoice = null)
    {
        voice = 0;
        if (_customizations.ValidateClan(input.Clan, input.Race, out _, out _).Length > 0)
            return false;
        if (!_customizations.IsGenderValid(input.Race, input.Gender))
            return false;
        if (input.BodyType.Value != 1)
            return false;

        var set = _customizations.AwaitedService.GetList(input.Clan, input.Gender);
        voice = set.Voices[0];
        if (inputVoice.HasValue && !set.Voices.Contains(inputVoice.Value))
            return false;

        foreach (var index in CustomizationExtensions.AllBasic)
        {
            if (!CustomizationService.IsCustomizationValid(set, input.Face, index, input[index]))
                return false;
        }

        if (input[CustomizeIndex.LegacyTattoo].Value != 0)
            return false;

        return true;
    }

    public bool Verify(in DatCharacterFile datFile)
    {
        var customize = datFile.Customize;
        if (!Verify(customize, out _, (byte)datFile.Voice))
            return false;

        if (datFile.Time < DateTimeOffset.UnixEpoch || datFile.Time > DateTimeOffset.UtcNow)
            return false;

        if (datFile.Checksum != datFile.CalculateChecksum())
            return false;

        return true;
    }
}
