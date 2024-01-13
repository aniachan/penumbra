using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Interface.Utility;
using ImGuiNET;
using OtterGui;
using OtterGui.Raii;
using OtterGui.Widgets;
using OtterGui.Classes;
using OtterGui.Services;
using Penumbra.Mods;
using Penumbra.Mods.Editor;
using Penumbra.Mods.Manager;
using Penumbra.Services;
using Penumbra.Mods.Settings;
using Penumbra.UI.ModsTab.Groups;

namespace Penumbra.UI.ModsTab;

public class ModPanelEditTab(
    ModManager modManager,
    ModFileSystemSelector selector,
    ModFileSystem fileSystem,
    Services.MessageService messager,
    FilenameService filenames,
    ModExportManager modExportManager,
    Configuration config,
    PredefinedTagManager predefinedTagManager,
    ModGroupEditDrawer groupEditDrawer,
    DescriptionEditPopup descriptionPopup,
    AddGroupDrawer addGroupDrawer)
    : ITab, IUiService
{

    private readonly TagButtons _modTags = new();

    private ModFileSystem.Leaf _leaf = null!;
    private Mod                _mod  = null!;

    public ReadOnlySpan<byte> Label
        => "Edit Mod"u8;

    public void DrawContent()
    {
        using var child = ImRaii.Child("##editChild", -Vector2.One);
        if (!child)
            return;

        _leaf = selector.SelectedLeaf!;
        _mod  = selector.Selected!;

        EditButtons();
        EditRegularMeta();
        UiHelpers.DefaultLineSpace();

        if (Input.Text("Mod Path", Input.Path, Input.None, _leaf.FullName(), out var newPath, 256, UiHelpers.InputTextWidth.X))
            try
            {
                fileSystem.RenameAndMove(_leaf, newPath);
            }
            catch (Exception e)
            {
                messager.NotificationMessage(e.Message, NotificationType.Warning, false);
            }

        UiHelpers.DefaultLineSpace();
        var sharedTagsEnabled     = predefinedTagManager.Count > 0;
        var sharedTagButtonOffset = sharedTagsEnabled ? ImGui.GetFrameHeight() + ImGui.GetStyle().FramePadding.X : 0;
        var tagIdx = _modTags.Draw("Mod Tags: ", "Edit tags by clicking them, or add new tags. Empty tags are removed.", _mod.ModTags,
            out var editedTag, rightEndOffset: sharedTagButtonOffset);
        if (tagIdx >= 0)
            modManager.DataEditor.ChangeModTag(_mod, tagIdx, editedTag);

        if (sharedTagsEnabled)
            predefinedTagManager.DrawAddFromSharedTagsAndUpdateTags(selector.Selected!.LocalTags, selector.Selected!.ModTags, false,
                selector.Selected!);

        UiHelpers.DefaultLineSpace();
        addGroupDrawer.Draw(_mod, UiHelpers.InputTextWidth.X);
        UiHelpers.DefaultLineSpace();

        groupEditDrawer.Draw(_mod);
        descriptionPopup.Draw();


        // Preview image upload
        if (ImGui.Button("Upload Custom Preview Images"))
        {
            string imagesFolderPath = Path.Combine(_selector.Selected!.ModPath.FullName, "images");

            // Create the "images" folder if it doesn't exist
            if (!Directory.Exists(imagesFolderPath))
                Directory.CreateDirectory(imagesFolderPath);

            // Show the file dialog picker to select custom image files
            _fileDialog.OpenFilePicker(
                "Select Custom Preview Images",
                "Image Files{.png,.jpg,.jpeg}",
                (success, filePaths) =>
                {
                    if (success)
                    {
                        foreach (string selectedImagePath in filePaths)
                        {
                            string destinationPath = Path.Combine(imagesFolderPath, Path.GetFileName(selectedImagePath));

                            try
                            {
                                // Copy the selected image file to the destination path
                                File.Copy(selectedImagePath, destinationPath, true);
                                _mod.PreviewImagePaths.Add(destinationPath);
                            }
                            catch (Exception e)
                            {
                                _messager.NotificationMessage(e.Message, NotificationType.Error);
                            }
                        }
                    }
                },
                selectionCountMax: 0, // Set the maximum selection count to unlimited
                startPath: null, // Start the file dialog in the default folder
                forceStartPath: false // Don't force the start path
            );
        }

        if (_mod.PreviewImagePaths.Count > 0)
        {
            ImGui.Text("Preview Images:");

            for (int i = 0; i < _mod.PreviewImagePaths.Count; i++)
            {
                string imagePath = _mod.PreviewImagePaths[i];

                // Create a unique ID for ImGui widgets
                string imageId = $"##Image{i}";

                ImGui.BeginGroup();

                // Create a text box for editing the image name
                string imageName = Path.GetFileNameWithoutExtension(imagePath);
                if (ImGui.InputText(imageId, ref imageName, 100))
                {
                    // Handle the image name change
                    if (string.IsNullOrWhiteSpace(imageName) || _mod.PreviewImagePaths.Any(p => p != imagePath && Path.GetFileNameWithoutExtension(p) == imageName))
                    {
                        // If the user tries to set the image name to blank or the name of another image, reset it to the original value.
                        imageName = Path.GetFileNameWithoutExtension(imagePath);
                    }
                    else
                    {
                        string newImagePath = Path.Combine(Path.GetDirectoryName(imagePath) ?? string.Empty, $"{imageName}.png");
                        // Rename the image file on disk
                        File.Move(imagePath, newImagePath);
                        // Update the imagePath with the new name
                        imagePath = newImagePath;
                        _mod.PreviewImagePaths[i] = newImagePath;
                    }
                }

                ImGui.SameLine();

                // Add a delete button
                if (ImGui.Button($"Delete{imageId}"))
                {
                    // Handle the delete button click
                    File.Delete(imagePath);
                    _mod.PreviewImagePaths.RemoveAt(i);
                    i--;
                }

                ImGui.EndGroup();
            }
        }
    }

    public void Reset()
    {
        MoveDirectory.Reset();
        Input.Reset();
    }

    /// <summary> The general edit row for non-detailed mod edits. </summary>
    private void EditButtons()
    {
        var buttonSize   = new Vector2(150 * UiHelpers.Scale, 0);
        var folderExists = Directory.Exists(_mod.ModPath.FullName);
        var tt = folderExists
            ? $"Open \"{_mod.ModPath.FullName}\" in the file explorer of your choice."
            : $"Mod directory \"{_mod.ModPath.FullName}\" does not exist.";
        if (ImGuiUtil.DrawDisabledButton("Open Mod Directory", buttonSize, tt, !folderExists))
            Process.Start(new ProcessStartInfo(_mod.ModPath.FullName) { UseShellExecute = true });

        ImGui.SameLine();
        if (ImGuiUtil.DrawDisabledButton("Reload Mod", buttonSize, "Reload the current mod from its files.\n"
              + "If the mod directory or meta file do not exist anymore or if the new mod name is empty, the mod is deleted instead.",
                false))
            modManager.ReloadMod(_mod);

        BackupButtons(buttonSize);
        MoveDirectory.Draw(modManager, _mod, buttonSize);

        UiHelpers.DefaultLineSpace();
    }

    private void BackupButtons(Vector2 buttonSize)
    {
        var backup = new ModBackup(modExportManager, _mod);
        var tt = ModBackup.CreatingBackup
            ? "Already exporting a mod."
            : backup.Exists
                ? $"Overwrite current exported mod \"{backup.Name}\" with current mod."
                : $"Create exported archive of current mod at \"{backup.Name}\".";
        if (ImGuiUtil.DrawDisabledButton("Export Mod", buttonSize, tt, ModBackup.CreatingBackup))
            backup.CreateAsync();

        ImGui.SameLine();
        tt = backup.Exists
            ? $"Delete existing mod export \"{backup.Name}\" (hold {config.DeleteModModifier} while clicking)."
            : $"Exported mod \"{backup.Name}\" does not exist.";
        if (ImGuiUtil.DrawDisabledButton("Delete Export", buttonSize, tt, !backup.Exists || !config.DeleteModModifier.IsActive()))
            backup.Delete();

        tt = backup.Exists
            ? $"Restore mod from exported file \"{backup.Name}\" (hold {config.DeleteModModifier} while clicking)."
            : $"Exported mod \"{backup.Name}\" does not exist.";
        ImGui.SameLine();
        if (ImGuiUtil.DrawDisabledButton("Restore From Export", buttonSize, tt, !backup.Exists || !config.DeleteModModifier.IsActive()))
            backup.Restore(modManager);
        if (backup.Exists)
        {
            ImGui.SameLine();
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextUnformatted(FontAwesomeIcon.CheckCircle.ToIconString());
            }

            ImGuiUtil.HoverTooltip($"Export exists in \"{backup.Name}\".");
        }
    }

    /// <summary> Anything about editing the regular meta information about the mod. </summary>
    private void EditRegularMeta()
    {
        if (Input.Text("Name", Input.Name, Input.None, _mod.Name, out var newName, 256, UiHelpers.InputTextWidth.X))
            modManager.DataEditor.ChangeModName(_mod, newName);

        if (Input.Text("Author", Input.Author, Input.None, _mod.Author, out var newAuthor, 256, UiHelpers.InputTextWidth.X))
            modManager.DataEditor.ChangeModAuthor(_mod, newAuthor);

        if (Input.Text("Version", Input.Version, Input.None, _mod.Version, out var newVersion, 32,
                UiHelpers.InputTextWidth.X))
            modManager.DataEditor.ChangeModVersion(_mod, newVersion);

        if (Input.Text("Website", Input.Website, Input.None, _mod.Website, out var newWebsite, 256,
                UiHelpers.InputTextWidth.X))
            modManager.DataEditor.ChangeModWebsite(_mod, newWebsite);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(UiHelpers.ScaleX3));

        var reducedSize = new Vector2(UiHelpers.InputTextMinusButton3, 0);
        if (ImGui.Button("Edit Description", reducedSize))
            descriptionPopup.Open(_mod);


        ImGui.SameLine();
        var fileExists = File.Exists(filenames.ModMetaPath(_mod));
        var tt = fileExists
            ? "Open the metadata json file in the text editor of your choice."
            : "The metadata json file does not exist.";
        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.FileExport.ToIconString()}##metaFile", UiHelpers.IconButtonSize, tt,
                !fileExists, true))
            Process.Start(new ProcessStartInfo(filenames.ModMetaPath(_mod)) { UseShellExecute = true });

        DrawOpenDefaultMod();
    }

    private void DrawOpenDefaultMod()
    {
        var file       = filenames.OptionGroupFile(_mod, -1, false);
        var fileExists = File.Exists(file);
        var tt = fileExists
            ? "Open the default mod data file in the text editor of your choice."
            : "The default mod data file does not exist.";
        if (ImGuiUtil.DrawDisabledButton("Open Default Data", UiHelpers.InputTextWidth, tt, !fileExists))
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }


    /// <summary> A text input for the new directory name and a button to apply the move. </summary>
    private static class MoveDirectory
    {
        private static string?           _currentModDirectory;
        private static NewDirectoryState _state = NewDirectoryState.Identical;

        public static void Reset()
        {
            _currentModDirectory = null;
            _state               = NewDirectoryState.Identical;
        }

        public static void Draw(ModManager modManager, Mod mod, Vector2 buttonSize)
        {
            ImGui.SetNextItemWidth(buttonSize.X * 2 + ImGui.GetStyle().ItemSpacing.X);
            var tmp = _currentModDirectory ?? mod.ModPath.Name;
            if (ImGui.InputText("##newModMove", ref tmp, 64))
            {
                _currentModDirectory = tmp;
                _state               = modManager.NewDirectoryValid(mod.ModPath.Name, _currentModDirectory, out _);
            }

            var (disabled, tt) = _state switch
            {
                NewDirectoryState.Identical      => (true, "Current directory name is identical to new one."),
                NewDirectoryState.Empty          => (true, "Please enter a new directory name first."),
                NewDirectoryState.NonExisting    => (false, $"Move mod from {mod.ModPath.Name} to {_currentModDirectory}."),
                NewDirectoryState.ExistsEmpty    => (false, $"Move mod from {mod.ModPath.Name} to {_currentModDirectory}."),
                NewDirectoryState.ExistsNonEmpty => (true, $"{_currentModDirectory} already exists and is not empty."),
                NewDirectoryState.ExistsAsFile   => (true, $"{_currentModDirectory} exists as a file."),
                NewDirectoryState.ContainsInvalidSymbols => (true,
                    $"{_currentModDirectory} contains invalid symbols for FFXIV."),
                _ => (true, "Unknown error."),
            };
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton("Rename Mod Directory", buttonSize, tt, disabled) && _currentModDirectory != null)
            {
                modManager.MoveModDirectory(mod, _currentModDirectory);
                Reset();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "The mod directory name is used to correspond stored settings and sort orders, otherwise it has no influence on anything that is displayed.\n"
              + "This can currently not be used on pre-existing folders and does not support merges or overwriting.");
        }
    }

    /// <summary> Handles input text and integers in separate fields without buffers for every single one. </summary>
    private static class Input
    {
        // Special field indices to reuse the same string buffer.
        public const int None        = -1;
        public const int Name        = -2;
        public const int Author      = -3;
        public const int Version     = -4;
        public const int Website     = -5;
        public const int Path        = -6;
        public const int Description = -7;

        // Temporary strings
        private static string?      _currentEdit;
        private static ModPriority? _currentGroupPriority;
        private static int          _currentField = None;
        private static int          _optionIndex  = None;

        public static void Reset()
        {
            _currentEdit          = null;
            _currentGroupPriority = null;
            _currentField         = None;
            _optionIndex          = None;
        }

        public static bool Text(string label, int field, int option, string oldValue, out string value, uint maxLength, float width)
        {
            var tmp = field == _currentField && option == _optionIndex ? _currentEdit ?? oldValue : oldValue;
            ImGui.SetNextItemWidth(width);

            if (ImGui.InputText(label, ref tmp, maxLength))
            {
                _currentEdit  = tmp;
                _optionIndex  = option;
                _currentField = field;
            }

            if (ImGui.IsItemDeactivatedAfterEdit() && _currentEdit != null)
            {
                var ret = _currentEdit != oldValue;
                value = _currentEdit;
                Reset();
                return ret;
            }

            value = string.Empty;
            return false;
        }

        public static bool Priority(string label, int field, int option, ModPriority oldValue, out ModPriority value, float width)
        {
            var tmp = (field == _currentField && option == _optionIndex ? _currentGroupPriority ?? oldValue : oldValue).Value;
            ImGui.SetNextItemWidth(width);
            if (ImGui.InputInt(label, ref tmp, 0, 0))
            {
                _currentGroupPriority = new ModPriority(tmp);
                _optionIndex          = option;
                _currentField         = field;
            }

            if (ImGui.IsItemDeactivatedAfterEdit() && _currentGroupPriority != null)
            {
                var ret = _currentGroupPriority != oldValue;
                value = _currentGroupPriority.Value;
                Reset();
                return ret;
            }

            value = ModPriority.Default;
            return false;
        }
    }
}
