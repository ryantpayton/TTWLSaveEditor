using AdonisUI;
using AutoUpdaterDotNET;
using Microsoft.Win32;
using OakSave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using WonderlandsSaveEditor.Helpers;
using WonderlandsTools;
using WonderlandsTools.GameData;
using WonderlandsTools.GameData.Items;
using Xceed.Wpf.Toolkit;
using MessageBox = AdonisUI.Controls.MessageBox;
using MessageBoxButton = AdonisUI.Controls.MessageBoxButton;
using MessageBoxImage = AdonisUI.Controls.MessageBoxImage;
using MessageBoxResult = AdonisUI.Controls.MessageBoxResult;

namespace WonderlandsSaveEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        #region Databinding Data
        public static string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public static RoutedCommand DuplicateCommand { get; } = new RoutedCommand();
        public static RoutedCommand DeleteCommand { get; } = new RoutedCommand();

        public int MaximumXP { get; } = PlayerXP._XPMaximumLevel;
        public int MinimumXP { get; } = PlayerXP._XPMinimumLevel;
        public int MaximumMayhemLevel { get; } = MayhemLevel.MaximumLevel;
        public bool SaveLoaded { get; set; } = false;
        public bool ShowDebugMaps { get; set; } = false;

        private bool _ForceLegitParts = false;

        public bool ForceLegitParts
        {
            get { return _ForceLegitParts; }
            set
            {
                _ForceLegitParts = value;

                RefreshBackpackView();

                ValidParts.Refresh();
                ValidGenerics.Refresh();
            }
        }

        public ListCollectionView ValidPlayerClasses => new ListCollectionView(WonderlandsSave.ValidClasses.Keys.ToList());
        public ListCollectionView ValidAbilityBranches => new ListCollectionView(WonderlandsSave.ValidAbilityBranches.Keys.ToList());
        public ListCollectionView ValidPlayerAspect => new ListCollectionView(WonderlandsSave.ValidPlayerAspect.Keys.ToList());
        public ListCollectionView ValidPlayerPronouns => new ListCollectionView(WonderlandsSave.ValidPlayerPronouns.Keys.ToList());

        public ListCollectionView SlotItems
        {
            get
            {
                // Hasn't loaded a save/profile yet
                if (SaveGame == null && Profile == null)
                    return null;

                ObservableCollection<StringSerialPair> px = new ObservableCollection<StringSerialPair>();
                List<int> usedIndexes = new List<int>();
                List<WonderlandsSerial> itemsToSearch;

                if (SaveGame != null)
                {
                    List<EquippedInventorySaveGameData> equippedItems = SaveGame.Character.EquippedInventoryLists;

                    foreach (EquippedInventorySaveGameData item in equippedItems)
                    {
                        if (!item.Enabled || item.InventoryListIndex < 0 || item.InventoryListIndex > SaveGame.InventoryItems.Count - 1)
                            continue;

                        usedIndexes.Add(item.InventoryListIndex);
                        px.Add(new StringSerialPair("Equipped", SaveGame.InventoryItems[item.InventoryListIndex]));
                    }

                    itemsToSearch = SaveGame.InventoryItems;
                }
                else
                {
                    itemsToSearch = Profile.BankItems;
                }

                for (int i = 0; i < itemsToSearch.Count; i++)
                {
                    // Ignore already used (equipped) indexes
                    if (usedIndexes.Contains(i))
                        continue;

                    WonderlandsSerial serial = itemsToSearch[i];

                    // Split the items out into groups, assume weapons because they're the most numerous and different.
                    string itemType = "Weapons";

                    if (serial.InventoryKey == null) itemType = "Other";
                    else if (serial.InventoryKey.Contains("_Amulet")) itemType = "Amulets";
                    else if (serial.InventoryKey.Contains("_Ring")) itemType = "Rings";
                    else if (serial.InventoryKey.Contains("_Shield")) itemType = "Shields";
                    else if (serial.InventoryKey.Contains("_SpellMod")) itemType = "Spell Mods";
                    else if (serial.InventoryKey.Contains("_Customization")) itemType = "Customizations";
                    // Test InventoryBalanceData to force Customizations category
                    //else if (serial.InventoryKey.Contains("InventoryBalanceData")) itemType = "Customizations";
                    else if (serial.InventoryKey.Contains("_Pauldron")) itemType = "Pauldrons";

                    px.Add(new StringSerialPair(itemType, serial));
                }

                ListCollectionView vx = new ListCollectionView(px);

                // Group them by the "type"
                vx.GroupDescriptions.Add(new PropertyGroupDescription("Val1"));

                return vx;
            }
        }

        public string[] ItemTypes = { "Normal", "Chaotic", "Volatile", "Primordial", "Ascended" };

        public ListCollectionView ValidItemTypes
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                List<string> ItemLists = new List<string>();
                ItemLists.AddRange(ItemTypes);

                return new ListCollectionView(ItemLists);
            }
        }

        public int GetItemTypeFromString(string value)
        {
            if (value == ItemTypes[0]) return 0;
            if (value == ItemTypes[1]) return 1;
            if (value == ItemTypes[2]) return 2;
            if (value == ItemTypes[3]) return 3;
            if (value == ItemTypes[4]) return 4;

            return 0;
        }

        public string SelectedItemTypes
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                return ItemTypes[SelectedSerial.ItemType];
            }
            set
            {
                if (SelectedSerial == null)
                    return;

                SelectedSerial.ItemType = GetItemTypeFromString(value);
            }
        }

        public ListCollectionView ValidBalances
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                string inventoryKey = SelectedSerial.InventoryKey;

                List<string> balances = InventoryKeyDB.KeyDictionary
                    .Where(x => x.Value.Equals(inventoryKey) && !x.Key.Contains("partset"))
                    .Select(x => InventorySerialDatabase.GetShortNameFromBalance(x.Key))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();

                return new ListCollectionView(balances);
            }
        }

        public string SelectedBalance
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                return InventorySerialDatabase.GetShortNameFromBalance(SelectedSerial.Balance);
            }
            set
            {
                if (SelectedSerial == null)
                    return;

                SelectedSerial.Balance = InventorySerialDatabase.GetBalanceFromShortName(value);
            }
        }

        public ListCollectionView ValidManufacturers
        {
            get
            {
                return new ListCollectionView(InventorySerialDatabase.GetManufacturers());
            }
        }

        public string SelectedManufacturer
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                string Manufacturer = SelectedSerial.Manufacturer;

                List<string> shortNames = InventorySerialDatabase.GetManufacturers();
                List<string> longNames = InventorySerialDatabase.GetManufacturers(false);

                try
                {
                    return shortNames[longNames.IndexOf(Manufacturer)];
                }
                catch
                {
                    return Manufacturer;
                }
            }
            set
            {
                if (SelectedSerial == null)
                    return;

                List<string> shortNames = InventorySerialDatabase.GetManufacturers();
                List<string> longNames = InventorySerialDatabase.GetManufacturers(false);

                SelectedSerial.Manufacturer = longNames[shortNames.IndexOf(value)];
            }
        }

        public ListCollectionView InventoryDatas => new ListCollectionView(InventorySerialDatabase.GetInventoryDatas());

        public string SelectedInventoryData
        {
            get
            {
                return SelectedSerial?.InventoryData.Split('.').LastOrDefault();
            }
            set
            {
                if (SelectedSerial == null)
                    return;

                List<string> shortNames = InventorySerialDatabase.GetInventoryDatas();
                List<string> longNames = InventorySerialDatabase.GetInventoryDatas(false);

                SelectedSerial.InventoryData = longNames[shortNames.IndexOf(value)];
            }
        }

        public WonderlandsSerial SelectedSerial { get; set; }

        public ListCollectionView ValidParts
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                List<string> validParts = new List<string>();

                if (!ForceLegitParts)
                    validParts = InventorySerialDatabase.GetPartsForInvKey(SelectedSerial.InventoryKey);
                else
                    validParts = InventorySerialDatabase.GetValidPartsForParts(SelectedSerial.InventoryKey, SelectedSerial.Parts);

                validParts = validParts.Select(x => x.Split('.').Last()).ToList();
                validParts.Sort();

                return new ListCollectionView(validParts);
            }
        }

        public ListCollectionView ValidGenerics
        {
            get
            {
                if (SelectedSerial == null)
                    return null;

                List<string> validParts = new List<string>();

                // In this case, balances are what actually restrict the items from their anointments.
                // Currently no generic parts actually have any excluders/dependencies but in the future they might so let's still enforce legit parts on them
                if (!ForceLegitParts)
                {
                    validParts = InventorySerialDatabase.GetPartsForInvKey("InventoryGenericPartData");
                }
                else
                {
                    validParts = InventorySerialDatabase.GetValidPartsForParts("InventoryGenericPartData", SelectedSerial.GenericParts);

                    List<string> vx = InventorySerialDatabase.GetValidPartsForParts("InventoryGenericPartData", SelectedSerial.Parts);
                    List<string> validGenerics = InventorySerialDatabase.GetValidGenericsForBalance(SelectedSerial.Balance);
                    string itemType = InventoryKeyDB.ItemTypeToKey.LastOrDefault(x => x.Value.Contains(SelectedSerial.InventoryKey)).Key;
                }

                return new ListCollectionView(validParts.Select(x => x.Split('.').Last()).ToList());
            }
        }

        public int MaximumBankSDUs { get { return SDU.MaximumBankSDUs; } }
        public int MaximumLostLootSDUs { get { return SDU.MaximumLostLoot; } }
        #endregion

        private static readonly string UpdateURL = "https://ryan.paytonglobal.com/Wonderlands/AutoUpdater.xml";

        private static Debug.DebugConsole dbgConsole;
        private readonly bool Launched = false;
        private readonly bool UseCustomUpdater = false;

        private readonly string InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tiny Tina's Wonderlands", "Saved", "SaveGames");

        /// <summary>
        /// <para>The current profile object</para>
        /// <para>Will be null if we haven't loaded a profile</para>
        /// </summary>
        public WonderlandsProfile Profile { get; set; } = null;

        /// <summary>
        /// <para>The current save game object</para>
        /// <para>Will be null if we loaded a profile instead of a save game</para>
        /// </summary>
        public WonderlandsSave SaveGame { get; set; } = null;

        public MainWindow()
        {
            Profile = null;
            SaveGame = null;

            InitializeComponent();

            DataContext = this;

            // Component initialization complete
            Launched = true;

            // Restore the dark mode state from last run
            CheckBox darkBox = (CheckBox)FindName("DarkModeBox");
            darkBox.IsChecked = Properties.Settings.Default.bDarkModeEnabled;

            DarkModeBox_Checked(darkBox, null);

            // Restore the Redux mode state from last run
            CheckBox reduxBox = (CheckBox)FindName("ReduxMode");
            bool bRedux = Properties.Settings.Default.bReduxModeEnabled;
            ReduxMode.IsChecked = bRedux;

            ReduxMode_Checked(reduxBox, null);

            dbgConsole = new Debug.DebugConsole();

            if (bRedux)
            {
                Title = "Tiny Tina's Wonderlands Save Editor **REDUX**";

                // Redux doesn't allow for different item types (Chaotic, Primordial, Ascended, etc.) so it is disabled here
                ComboBox rdxITComboBox = (ComboBox)FindName("cbItemType");
                rdxITComboBox.IsEnabled = false;

                // Redux is also only available on PC so platform selector is disabled
                ComboBox rdxPlatComboBox = (ComboBox)FindName("cbPlatform");
                rdxPlatComboBox.IsEnabled = false;

                // Redux max chaos level is 10
                IntegerUpDown iudChaosUnlocked = (IntegerUpDown)FindName("ChaosUnlockedLevel");
                iudChaosUnlocked.Maximum = 10;

                IntegerUpDown iudChaosCurrent = (IntegerUpDown)FindName("ChaosCurrentLevel");
                iudChaosCurrent.Maximum = 10;
            }

            // Initialize selected parts list
            SelectedParts = new List<string>();
            GenericSelectedParts = new List<string>();

            ((TabControl)FindName("TabCntrl")).SelectedIndex = ((TabControl)FindName("TabCntrl")).Items.Count - 1;

            tbUpdates.Inlines.Add(new Run() { Text = $"Current Version: V{Version}", FontWeight = FontWeights.Bold });
            tbUpdates.Inlines.Add(new LineBreak());
            tbUpdates.Inlines.Add(new LineBreak());

            try
            {
                string line;
                WebClient client = new WebClient();
                Stream stream = client.OpenRead("https://ryan.paytonglobal.com/Wonderlands/changelog.md");
                StreamReader reader = new StreamReader(stream);

                while ((line = reader.ReadLine()) != null)
                {
                    // Version
                    if (line.StartsWith("####"))
                    {
                        int s = line.IndexOf("V");
                        tbUpdates.Inlines.Add(new Run() { Text = line.Substring(s, line.Length - s), FontWeight = FontWeights.Bold });
                    }
                    // Change in the version
                    else if (line.StartsWith("-"))
                    {
                        tbUpdates.Inlines.Add(new Run() { Text = $"\t{line}" });
                        tbUpdates.Inlines.Add(new LineBreak());
                    }
                    else if (line == string.Empty)
                    {
                        tbUpdates.Inlines.Add(new LineBreak());
                    }
                }
            }
            catch (Exception ex)
            {
                tbUpdates.Inlines.Add(new Run() { Text = "Could not fetch changelog." });

#if DEBUG
                tbUpdates.Inlines.Add(new LineBreak());
                tbUpdates.Inlines.Add(new LineBreak());
                tbUpdates.Inlines.Add(new Run() { Text = "ERROR:", FontWeight = FontWeights.Bold });
                tbUpdates.Inlines.Add(new LineBreak());
                tbUpdates.Inlines.Add(new Run() { Text = ex.Message });
#endif
            }

            if (UseCustomUpdater)
                AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;

            AutoUpdater.RunUpdateAsAdmin = true;
            AutoUpdater.Start(UpdateURL);
        }

        #region Toolbar Interaction
        private void NewSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void OpenSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Get Redux mode status
            bool bRedux = Properties.Settings.Default.bReduxModeEnabled;

            // If not Redux, populate all platforms, else only PC.
            if (!bRedux)
            {
                Dictionary<Platform, string> PlatformFilters = new Dictionary<Platform, string>()
                {
                    { Platform.PC, "PC Tiny Tina's Wonderlands Save/Profile (*.sav)|*.sav" },
                    { Platform.PS4, "PS4 Tiny Tina's Wonderlands Save/Profile (*.sav)|*.sav" },
                    { Platform.JSON, "PS4 Save Wizard Tiny Tina's Wonderlands Save/Profile (*.*)|*.*"}
                };

                OpenFileDialog fileDialog = new OpenFileDialog
                {
                    Title = "Select Tiny Tina's Wonderlands Save/Profile",
                    Filter = string.Join("|", PlatformFilters.Values),
                    InitialDirectory = InitialDirectory,
                };

                if (fileDialog.ShowDialog() == true)
                {
                    Platform platform = PlatformFilters.Keys.ToArray()[fileDialog.FilterIndex - 1];
                    OpenSave(fileDialog.FileName, platform);
                }
            }
            else
            {
                Dictionary<Platform, string> PlatformFilters = new Dictionary<Platform, string>()
                {
                    { Platform.PC, "PC Tiny Tina's Wonderlands Save/Profile (*.sav)|*.sav" }
                };

                OpenFileDialog fileDialog = new OpenFileDialog
                {
                    Title = "Select Tiny Tina's Wonderlands Save/Profile",
                    Filter = string.Join("|", PlatformFilters.Values),
                    InitialDirectory = InitialDirectory,
                };

                if (fileDialog.ShowDialog() == true)
                {
                    Platform platform = PlatformFilters.Keys.ToArray()[fileDialog.FilterIndex - 1];
                    OpenSave(fileDialog.FileName, platform);
                }
            }
        }

        private void OpenSave(string filePath, Platform platform = Platform.PC)
        {
            try
            {
                // Reload the save just for safety, this way we're getting the "saved" version on a save.
                object saveObj = WonderlandsTools.WonderlandsTools.LoadFileFromDisk(filePath, platform);
                Console.WriteLine($"Reading a save of type: {saveObj.GetType()}");

                if (saveObj.GetType() == typeof(WonderlandsProfile))
                {
                    Profile = (WonderlandsProfile)saveObj;
                    SaveGame = null;
                    SaveLoaded = false;

                    // Profile tab
                    TabCntrl.SelectedIndex = 5;

                }
                else
                {
                    SaveGame = (WonderlandsSave)saveObj;
                    Profile = null;
                    SaveLoaded = true;

                    // General tab
                    TabCntrl.SelectedIndex = 0;
                }

                ((TabItem)FindName("RawTabItem")).IsEnabled = true;
                ((TabItem)FindName("InventoryTabItem")).IsEnabled = true;

                ((Button)FindName("SaveBtn")).IsEnabled = true;
                ((Button)FindName("SaveAsBtn")).IsEnabled = true;

                // Refresh the bindings on the GUI
                DataContext = null;
                DataContext = this;

                BackpackListView.ItemsSource = null;
                BackpackListView.ItemsSource = SlotItems;

                RefreshBackpackView();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load save ({0}) :: {1}", filePath, ex.Message);
                Console.WriteLine(ex.StackTrace);

                MessageBox.Show($"Error parsing save: {ex.Message}", "Save Parse Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveOpenedFile()
        {
            if (SaveGame != null)
            {
                WonderlandsTools.WonderlandsTools.WriteFileToDisk(SaveGame);
            }
            else if (Profile != null)
            {
                // TODO: Use the correct formula
                // TODO: Split this to an function
                int spentPoints = 0;

                foreach (int points in Profile.Profile.PlayerPrestige.PointsSpentByIndexOrders)
                    spentPoints += points;

                Profile.Profile.PlayerPrestige.PrestigeExperience = PlayerXP.GetPointsForMythPoints(spentPoints);

                WonderlandsTools.WonderlandsTools.WriteFileToDisk(Profile);
            }

#if DEBUG
            OpenSave(SaveGame == null ? Profile.filePath : SaveGame.filePath);
#endif
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Saving save...");
            SaveOpenedFile();
        }

        private void SaveAsBtn_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Saving save as...");
            SaveFileDialog saveFileDialog;

            if (SaveGame.Platform == Platform.JSON)
            {
                saveFileDialog = new SaveFileDialog()
                {
                    Title = "Save Tiny Tina's Wonderlands Save/Profile",
                    Filter = "PS4 Save Wizard Tiny Tina's Wonderlands Save/Profile (*)|*",
                    InitialDirectory = InitialDirectory
                };
            }
            else
            {
                saveFileDialog = new SaveFileDialog()
                {
                    Title = "Save Tiny Tina's Wonderlands Save/Profile",
                    Filter = "Tiny Tina's Wonderlands Save/Profile (*.sav)|*.sav",
                    InitialDirectory = InitialDirectory
                };
            }

            // Update the file like this so that way, once you do a "Save As", it still changes the saved-as file instead of the originally opened file.
            if (saveFileDialog.ShowDialog() == true)
            {
                if (SaveGame != null) SaveGame.filePath = saveFileDialog.FileName;
                else if (Profile != null) Profile.filePath = saveFileDialog.FileName;
            }

            SaveOpenedFile();
        }

        private void DbgBtn_Click(object sender, RoutedEventArgs e)
        {
            dbgConsole.Show();
        }
        #endregion

        private void AdonisWindow_Closed(object sender, EventArgs e)
        {
            Console.WriteLine("Closing program...");

            // Release the console writer on close to avoid memory issues
            dbgConsole.consoleRedirectWriter.Release();

            // Need to set this boolean in order to actually close the program
            dbgConsole.bClose = true;
            dbgConsole.Close();
        }

        #region Theme Toggling
        private void DarkModeBox_Checked(object sender, RoutedEventArgs e)
        {
            if (Launched)
            {
                bool bChecked = (bool)((CheckBox)sender).IsChecked;
                ResourceLocator.SetColorScheme(Application.Current.Resources, bChecked ? ResourceLocator.DarkColorScheme : ResourceLocator.LightColorScheme);

                // Update the settings now
                Properties.Settings.Default.bDarkModeEnabled = bChecked;
                Properties.Settings.Default.Save();

            }
        }
        #endregion

        #region Interactions
        #region General
        private void RandomizeGUIDBtn_Click(object sender, RoutedEventArgs e)
        {
            Guid newGUID = Guid.NewGuid();
            GUIDTextBox.Text = newGUID.ToString().Replace("-", "").ToUpper();

            // Super kludge to get SaveGameGuid to update, fixes the "GUID not saving" problem.
            GUIDTextBox.Focus();
            RandomizeGUIDBtn.Focus();
        }

        private void AdjustSaveLevelsBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Title = "Select Tiny Tina's Wonderlands Saves/Profiles",
                Filter = "Tiny Tina's Wonderlands Save/Profile (*.sav)|*.sav",
                InitialDirectory = InitialDirectory,
                Multiselect = true
            };

            if (fileDialog.ShowDialog() != true)
                return;

            Controls.IntegerMessageBox msgBox = new Controls.IntegerMessageBox("Enter a level to sync saves to: ", "Level: ", MinimumXP, MaximumXP, MaximumXP)
            {
                Owner = this
            };

            msgBox.ShowDialog();

            if (!msgBox.Succeeded)
                return;

            int level = msgBox.Result;

            foreach (string file in fileDialog.FileNames)
            {
                try
                {
                    if (!(WonderlandsTools.WonderlandsTools.LoadFileFromDisk(file) is WonderlandsSave save))
                    {
                        Console.WriteLine("Read in file from \"{0}\"; Incorrect type: {1}");
                        continue;
                    }

                    save.Character.ExperiencePoints = PlayerXP.GetPointsForXPLevel(level);

                    WonderlandsTools.WonderlandsTools.WriteFileToDisk(save, false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to adjust level of save: \"{0}\"\n{1}", ex.Message, ex.StackTrace);
                }
            }
        }

        private void BackupAllSavesBtn_Click(object sender, RoutedEventArgs e)
        {
            // Ask the user for all the saves to backup
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Title = "Backup Tiny Tina's Wonderlands Saves/Profiles",
                Filter = "Tiny Tina's Wonderlands Save/Profile (*.sav)|*.sav",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tiny Tina's Wonderlands", "Saved", "SaveGames"),
                Multiselect = true
            };

            if (fileDialog.ShowDialog() != true)
                return;

            // Ask the user for a ZIP output
            SaveFileDialog outDialog = new SaveFileDialog
            {
                Title = "Backup Outputs",
                Filter = "ZIP file|*.zip",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tiny Tina's Wonderlands", "Saved", "SaveGames"),
                RestoreDirectory = true,
            };

            if (outDialog.ShowDialog() != true)
                return;

            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Finally back up all of the saves (Using a ZIP)
                using (FileStream ms = new FileStream(outDialog.FileName, FileMode.Create))
                {
                    using (ZipArchive archive = new ZipArchive(ms, ZipArchiveMode.Create))
                    {
                        foreach (string path in fileDialog.FileNames)
                        {
                            string fileName = Path.GetFileName(path);
                            ZipArchiveEntry saveEntry = archive.CreateEntry(fileName, CompressionLevel.Optimal);

                            using (BinaryWriter writer = new BinaryWriter(saveEntry.Open()))
                            {
                                byte[] data = File.ReadAllBytes(path);
                                writer.Write(data);
                            }
                        }
                    }
                }

                Console.WriteLine("Backed up all saves: {0} to ZIP: {1}", string.Join(",", fileDialog.FileNames), outDialog.FileName);
            }
            finally
            {
                // Make sure that in the event of an exception, that the mouse cursor gets restored.
                Mouse.OverrideCursor = null;
            }
        }
        #endregion

        #region Character
        private void CharacterClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string str = e.AddedItems.OfType<string>().FirstOrDefault();

            if (str == null || str == default)
                return;
        }
        #endregion

        #region Fast Travel
        private void DbgMapBox_StateChange(object sender, RoutedEventArgs e)
        {
            VisitedTeleportersGrpBox.DataContext = null;
            VisitedTeleportersGrpBox.DataContext = this;
        }

        private void FastTravelChkBx_StateChanged(object sender, RoutedEventArgs e)
        {
            if (sender == null || SaveGame == null)
                return;

            CheckBox senderBx = (CheckBox)sender;

            if (senderBx.Content.GetType() != typeof(TextBlock))
                return;

            bool bFastTravelEnabled = senderBx.IsChecked == true;
            string fastTravelToChange = ((senderBx.Content as TextBlock).Text);
            string assetPath = DataPathTranslations.FastTravelTranslations.FirstOrDefault(x => x.Value == fastTravelToChange).Key;

            Console.WriteLine("Changed state of {0} ({2}) to {1}", fastTravelToChange, bFastTravelEnabled, assetPath);

            int amtOfPlaythroughs = SaveGame.Character.ActiveTravelStationsForPlaythroughs.Count - 1;
            int playthroughIndex = SelectedPlaythroughBox.SelectedIndex;

            if (amtOfPlaythroughs < SelectedPlaythroughBox.SelectedIndex)
                SaveGame.Character.ActiveTravelStationsForPlaythroughs.Add(new OakSave.PlaythroughActiveFastTravelSaveData());

            List<OakSave.ActiveFastTravelSaveData> travelStations = SaveGame.Character.ActiveTravelStationsForPlaythroughs[playthroughIndex].ActiveTravelStations;

            if (bFastTravelEnabled)
            {
                travelStations.Add(new OakSave.ActiveFastTravelSaveData()
                {
                    ActiveTravelStationName = assetPath,
                    Blacklisted = false
                });
            }
            else
            {
                travelStations.RemoveAll(x => x.ActiveTravelStationName == assetPath);
            }

            return;
        }

        private void EnableAllTeleportersBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (BoolStringPair bsp in TeleportersItmCntrl.Items)
            {
                ContentPresenter presenter = (ContentPresenter)TeleportersItmCntrl.ItemContainerGenerator.ContainerFromItem(bsp);
                presenter.ApplyTemplate();

                CheckBox chkBox = presenter.ContentTemplate.FindName("FastTravelChkBx", presenter) as CheckBox;
                chkBox.IsChecked = true;
            }
        }

        private void DisableAllTeleportersBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (BoolStringPair bsp in TeleportersItmCntrl.Items)
            {
                ContentPresenter presenter = (ContentPresenter)TeleportersItmCntrl.ItemContainerGenerator.ContainerFromItem(bsp);
                presenter.ApplyTemplate();

                CheckBox chkBox = presenter.ContentTemplate.FindName("FastTravelChkBx", presenter) as CheckBox;
                chkBox.IsChecked = false;
            }
        }
        #endregion

        #region Backpack / Bank
        private void RefreshBackpackView()
        {
            // Need to change the data context real quick to make the GUI update
            Grid grid = (Grid)FindName("SerialContentsGrid");
            grid.DataContext = null;
            grid.DataContext = this;

            Label partsLabel = (Label)FindName("PartsLabel");
            partsLabel.DataContext = null;
            partsLabel.DataContext = this;
            partsLabel = (Label)FindName("GenericPartsLabel");
            partsLabel.DataContext = null;
            partsLabel.DataContext = this;

            Button addPartBtn = (Button)FindName("GenericPartsAddBtn");
            addPartBtn.DataContext = null;
            addPartBtn.DataContext = this;
            addPartBtn = (Button)FindName("PartsAddBtn");
            addPartBtn.DataContext = null;
            addPartBtn.DataContext = this;

            Button delPartBtn = (Button)FindName("GenericPartsDelBtn");
            delPartBtn.DataContext = null;
            delPartBtn.DataContext = this;
            delPartBtn = (Button)FindName("PartsDelBtn");
            delPartBtn.DataContext = null;
            delPartBtn.DataContext = this;

            Button delAllPartBtn = (Button)FindName("GenericPartsDelAllBtn");
            delAllPartBtn.DataContext = null;
            delAllPartBtn.DataContext = this;
            delAllPartBtn = (Button)FindName("PartsDelAllBtn");
            delAllPartBtn.DataContext = null;
            delAllPartBtn.DataContext = this;
        }

        private void IntegerUpDown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue == null || e.OldValue == null)
                return;

            RefreshBackpackView();
        }

        private void BackpackListView_Selected(object sender, EventArgs e)
        {
            if (BackpackListView.Items.Count < 1 || BackpackListView.SelectedValue == null)
                return;

            ListView listView = (sender as ListView);
            StringSerialPair svp = (StringSerialPair)listView.SelectedValue;
            SelectedSerial = svp.Val2;

            // Scroll to the selected item (In case of duplication, etc.)
            listView.ScrollIntoView(listView.SelectedItem);

            // Clear selected parts list
            SelectedParts.Clear();
            GenericSelectedParts.Clear();

            RefreshBackpackView();
        }

        private void BackpackListView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
                return;

            // This janky bit of logic allows us to scroll on hover over the items of the ListView as well
            ListView listview = (sender as ListView);
            ScrollViewer scrollViewer = listview.FindVisualChildren<ScrollViewer>().First();

            // Multiply the value by 0.7 because just the delta value can be a bit much
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.7));

            // Make sure no other elements can handle the events
            e.Handled = true;
        }

        private void NewItemBtn_Click(object sender, RoutedEventArgs e)
        {
            Controls.ItemBalanceChanger changer = new Controls.ItemBalanceChanger() { Owner = this };
            changer.ShowDialog();

            // The user actually hit the save button and we have data about the item
            if (changer.SelectedInventoryData != null)
            {
                WonderlandsSerial serial = WonderlandsSerial.CreateSerialFromBalanceData(changer.SelectedBalance);

                if (serial == null)
                    return;

                serial.InventoryData = changer.SelectedInventoryData;

                // Set a manufacturer so that way the bindings don't lose their mind
                serial.Manufacturer = InventorySerialDatabase.GetManufacturers().FirstOrDefault();

                if (Profile == null)
                    SaveGame.AddItem(serial);
                else
                    Profile.BankItems.Add(serial);

                BackpackListView.ItemsSource = null;
                BackpackListView.ItemsSource = SlotItems;

                RefreshBackpackView();
            }
        }

        private void PasteCodeBtn_Click(object sender, RoutedEventArgs e)
        {
            string clipboardData = string.Empty;

            // This is to catch Clipboard COM errors
            while (clipboardData == string.Empty)
            {
                try
                {
                    clipboardData = Clipboard.GetText();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            int firstPart;

            if ((firstPart = clipboardData.IndexOf("WL(")) != -1)
            {
                int lastPart;
                string serialCodeCheck = clipboardData.Substring(firstPart, clipboardData.Length - firstPart);

                if ((lastPart = serialCodeCheck.IndexOf(")")) != -1)
                {
                    string serialCode = serialCodeCheck.Substring(0, lastPart + 1);
                    Console.WriteLine("Pasting serial code: {0}", serialCode);

                    CreateItemFromCode(serialCode);
                }
            }
            else
            {
                MessageBox.Show("Invalid WL item code.");
            }
        }

        private void CreateItemFromCode(string replacement)
        {
            try
            {
                WonderlandsSerial item = WonderlandsSerial.DecryptSerial(replacement);

                if (item == null)
                    return;

                if (Profile == null)
                    SaveGame.AddItem(item);
                else
                    Profile.BankItems.Add(item);

                BackpackListView.ItemsSource = null;
                BackpackListView.ItemsSource = SlotItems;
                BackpackListView.Items.Refresh();

                RefreshBackpackView();

                StringSerialPair selectedValue = BackpackListView.Items.Cast<StringSerialPair>().Where(x => ReferenceEquals(x.Val2, item)).LastOrDefault();
                BackpackListView.SelectedValue = selectedValue;
            }
            catch (WonderlandsTools.WonderlandsTools.WonderlandsExceptions.SerialParseException ex)
            {
                Console.WriteLine($"Exception ({ex.Message}) parsing serial: {ex}");

                if (ex.knowCause)
                    MessageBox.Show($"Error parsing serial: {ex.Message}", "Serial Parse Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                Console.WriteLine($"Exception ({message}) parsing serial: {ex}");
                MessageBox.Show($"Error parsing serial: {message}", "Serial Parse Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SyncEquippedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SaveGame == null)
                return;

            int levelToSync = PlayerXP.GetLevelForPoints(SaveGame.Character.ExperiencePoints);

            foreach (var equipData in SaveGame.Character.EquippedInventoryLists)
            {
                if (!equipData.Enabled || equipData.InventoryListIndex < 0 || equipData.InventoryListIndex > SaveGame.InventoryItems.Count - 1)
                    continue;

                // Sync the level onto the item
                SaveGame.InventoryItems[equipData.InventoryListIndex].Level = levelToSync;
            }

            RefreshBackpackView();
        }

        private void SyncAllBtn_Click(object sender, RoutedEventArgs e)
        {
            int levelToSync;

            if (Profile != null)
            {
                Controls.IntegerMessageBox msgBox = new Controls.IntegerMessageBox("Please enter a level to sync your items for syncing", "Level: ", 0, MaximumXP, MaximumXP)
                {
                    Owner = this
                };

                msgBox.ShowDialog();

                if (!msgBox.Succeeded)
                    return;

                levelToSync = msgBox.Result;
            }
            else
            {
                levelToSync = PlayerXP.GetLevelForPoints(SaveGame.Character.ExperiencePoints);
            }

            foreach (WonderlandsSerial item in Profile == null ? SaveGame.InventoryItems : Profile.BankItems)
            {
                Console.WriteLine($"Syncing level for item ({item.UserFriendlyName}) from {item.Level} to {levelToSync}");

                item.Level = levelToSync;
            }

            RefreshBackpackView();
        }

        private void CopyItem_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string serialString = GetSelectedItemSerial();

            if (serialString.Length == 0)
                return;

            Console.WriteLine("Copying selected item code: {0}", serialString);

            string copyData = $"{SelectedSerial.UserFriendlyName}:\r\n{serialString}";

            // Copy it to the clipboard
            Clipboard.SetDataObject(copyData, true);
        }

        private string GetSelectedItemSerial()
        {
            if (BackpackListView.SelectedValue == null)
                return "";

            StringSerialPair svp = (StringSerialPair)BackpackListView.SelectedValue;
            SelectedSerial = svp.Val2;

            // Copy the code with a 0 seed
            return SelectedSerial.EncryptSerial(0);
        }

        private void PasteItem_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            PasteCodeBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void DuplicateItem_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            string serialString = GetSelectedItemSerial();

            if (serialString.Length == 0)
                return;

            Console.WriteLine("Duplicating selected item code: {0}", serialString);

            CreateItemFromCode(serialString);
        }

        private void DeleteBinding_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!(BackpackListView.SelectedValue is StringSerialPair svp))
                return;

            Console.WriteLine("Deleting item: {0} ({1})", svp.Val1, svp.Val2.UserFriendlyName);

            int idx = (SaveGame == null ? Profile.BankItems.FindIndex(x => ReferenceEquals(x, svp.Val2)) : SaveGame.InventoryItems.FindIndex(x => ReferenceEquals(x, svp.Val2)));

            if (SaveGame == null)
            {
                Profile.BankItems.RemoveAt(idx);
            }
            else
            {
                // We need to preemptively adjust the equipped inventory lists so that way the equipped items stay consistent with the removed items
                // TODO: Consider putting this into WonderlandsTools instead?
                int eilIndex = SaveGame.InventoryItems.FindIndex(x => ReferenceEquals(x, svp.Val2));

                foreach (EquippedInventorySaveGameData vx in SaveGame.Character.EquippedInventoryLists)
                {
                    if (vx.InventoryListIndex == eilIndex)
                        vx.InventoryListIndex = -1;
                    else if (vx.InventoryListIndex > eilIndex)
                        vx.InventoryListIndex -= 1;
                }

                SaveGame.DeleteItem(svp.Val2);

                if (SaveGame.InventoryItems.Count <= 0)
                    SelectedSerial = null;
            }

            BackpackListView.ItemsSource = null;
            BackpackListView.ItemsSource = SlotItems;
            BackpackListView.Items.Refresh();

            RefreshBackpackView();
        }

        private void ChangeItemTypeBtn_Click(object sender, RoutedEventArgs e)
        {
            string itemKey = InventoryKeyDB.GetKeyForBalance(InventorySerialDatabase.GetBalanceFromShortName(SelectedBalance));
            string itemType = InventoryKeyDB.ItemTypeToKey.Where(x => x.Value.Contains(itemKey)).Select(x => x.Key).FirstOrDefault();

            Controls.ItemBalanceChanger changer = new Controls.ItemBalanceChanger(itemType, SelectedBalance) { Owner = this };

            changer.ShowDialog();

            // The user actually hit the save button and we have data about the item
            if (changer.SelectedInventoryData != null)
            {
                SelectedInventoryData = changer.SelectedInventoryData;
                SelectedBalance = changer.SelectedBalance;

                RefreshBackpackView();
            }
        }

        private void ChangeTypeBtn_Click(object sender, RoutedEventArgs e)
        {
            string itemKey = InventoryKeyDB.GetKeyForBalance(InventorySerialDatabase.GetBalanceFromShortName(SelectedBalance));
            string itemType = InventoryKeyDB.ItemTypeToKey.Where(x => x.Value.Contains(itemKey)).Select(x => x.Key).FirstOrDefault();

            Controls.ItemBalanceChanger changer = new Controls.ItemBalanceChanger(itemType, SelectedBalance) { Owner = this };

            changer.ShowDialog();

            // The user actually hit the save button and we have data about the item
            if (changer.SelectedInventoryData != null)
            {
                SelectedInventoryData = changer.SelectedInventoryData;
                SelectedBalance = changer.SelectedBalance;

                RefreshBackpackView();
            }
        }

        private void AddItemPartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSerial == null)
                return;

            Button btn = (Button)sender;
            ListView obj = (ListView)FindName(btn.Name.Replace("AddBtn", "") + "ListView");
            string propertyName = obj.Name.Split(new string[] { "ListView" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (propertyName == default)
                return;

            List<string> parts = (List<string>)SelectedSerial.GetType().GetProperty(propertyName).GetValue(SelectedSerial, null);

            parts.Add(InventorySerialDatabase.GetPartFromShortName(
                (propertyName == "Parts" ? SelectedSerial.InventoryKey : "InventoryGenericPartData"),
                (propertyName == "Parts" ? ValidParts : ValidGenerics).SourceCollection.Cast<string>().FirstOrDefault())
            );

            // Update the valid parts
            ValidParts.Refresh();
            ValidGenerics.Refresh();

            obj.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
            RefreshBackpackView();
        }

        private void DuplicateItemPartBtn_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            ListView obj = ((ListView)FindName(btn.Name.Replace("DupBtn", "") + "ListView"));

            string propertyName = obj.Name.Split(new string[] { "ListView" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (propertyName == default)
                return;

            List<string> parts = (List<string>)SelectedSerial.GetType().GetProperty(propertyName).GetValue(SelectedSerial, null);

            string longName = parts.Find(x => x.EndsWith(btn.DataContext.ToString()));
            string category = propertyName == "Parts" ? SelectedSerial.InventoryKey : "InventoryGenericPartData";
            string p = InventorySerialDatabase.GetPartFromShortName(category, longName);

            parts.Add(p);

            // Update the valid parts
            ValidParts.Refresh();
            ValidGenerics.Refresh();

            obj.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
            RefreshBackpackView();
        }

        private void DeleteItemPartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSerial == null)
                return;

            Button btn = (Button)sender;
            ListView obj = (ListView)FindName(btn.Name.Replace("DelBtn", "") + "ListView");
            string propertyName = obj.Name.Split(new string[] { "ListView" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (propertyName == default)
                return;

            List<string> list = propertyName == "Parts" ? SelectedParts : GenericSelectedParts;

            if (list.Count == 0) return;

            List<string> parts = (List<string>)SelectedSerial.GetType().GetProperty(propertyName).GetValue(SelectedSerial, null);
            List<string> selectedParts = new List<string>();

            foreach (string part in parts)
            {
                string[] strings = part.Split('.');
                string s = strings[strings.Length - 1];

                if (list.Contains(s))
                    selectedParts.Add(part);
            }

            foreach (string part in selectedParts)
            {
                // Remove the part
                int index = parts.FindIndex(x => x.Equals(part));
                parts.RemoveAt(index);
            }

            // Empty the selected parts since we are going to refresh the view
            if (propertyName == "Parts") SelectedParts.Clear(); else GenericSelectedParts.Clear();

            // Update the valid parts
            ValidParts.Refresh();
            ValidGenerics.Refresh();

            obj.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
            RefreshBackpackView();
        }

        private void DeleteItemPartSingleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSerial == null)
                return;

            Button btn = (Button)sender;
            ListView obj = (ListView)FindName(btn.Name.Replace("DelBtnSingle", "") + "ListView");
            string propertyName = obj.Name.Split(new string[] { "ListView" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (propertyName == default)
                return;

            List<string> parts = (List<string>)SelectedSerial.GetType().GetProperty(propertyName).GetValue(SelectedSerial, null);

            foreach (string part in parts)
            {
                string[] strings = part.Split('.');
                string s = strings[strings.Length - 1];

                if (s == btn.DataContext.ToString())
                {
                    parts.Remove(part);
                    break;
                }
            }

            // Update the valid parts
            ValidParts.Refresh();
            ValidGenerics.Refresh();

            obj.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
            RefreshBackpackView();
        }

        private void PartsDelAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSerial == null)
                return;

            Button btn = (Button)sender;
            ListView obj = (ListView)FindName(btn.Name.Replace("DelAllBtn", "") + "ListView");
            string propertyName = obj.Name.Split(new string[] { "ListView" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (propertyName == default)
                return;

            List<string> parts = (List<string>)SelectedSerial.GetType().GetProperty(propertyName).GetValue(SelectedSerial, null);

            while (parts.Count > 0)
            {
                // Remove the part
                parts.RemoveAt(0);
            }

            // Empty the selected parts since we are going to refresh the view
            if (propertyName == "GenericParts")
                GenericSelectedParts.Clear();

            if (propertyName == "Parts")
                SelectedParts.Clear();

            // Update the valid parts
            ValidParts.Refresh();
            ValidGenerics.Refresh();

            obj.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
            RefreshBackpackView();
        }

        // This bit of logic is here so that way the ListView's selected value stays up to date with the ComboBox's selected value
        private void ComboBox_DropDownChanged(object sender, EventArgs e)
        {
            ComboBox box = (ComboBox)sender;
            ListView parent = box.FindParent<ListView>();

            if (parent == null)
                return;

            parent.SelectedValue = box.SelectedValue;
        }

        public List<string> SelectedParts { get; set; }

        public List<string> GenericSelectedParts { get; set; }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox box = (CheckBox)sender;
            string part = (string)box.DataContext;
            bool addPart = box.IsChecked == true && !SelectedParts.Contains(part);

            if (addPart)
                SelectedParts.Add(part);
            else
                SelectedParts.Remove(part);

            Button delPartBtn = (Button)FindName("GenericPartsDelBtn");
            delPartBtn.DataContext = null;
            delPartBtn.DataContext = this;
            delPartBtn = (Button)FindName("PartsDelBtn");
            delPartBtn.DataContext = null;
            delPartBtn.DataContext = this;
        }

        private void GenericCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox box = (CheckBox)sender;
            string part = (string)box.DataContext;
            bool addPart = box.IsChecked == true && !GenericSelectedParts.Contains(part);

            if (addPart)
                GenericSelectedParts.Add(part);
            else
                GenericSelectedParts.Remove(part);

            Button delPartBtn = (Button)FindName("GenericPartsDelBtn");
            delPartBtn.DataContext = null;
            delPartBtn.DataContext = this;
            delPartBtn = (Button)FindName("PartsDelBtn");
            delPartBtn.DataContext = null;
            delPartBtn.DataContext = this;
        }

        private void ItemPart_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox box = (ComboBox)sender;
            ListView parent = box.FindParent<ListView>();

            if (parent == null)
                return;

            string propertyName = parent.Name.Split(new string[] { "ListView" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (propertyName == default)
                return;

            ListCollectionView list = propertyName == "Parts" ? ValidParts : ValidGenerics;

            if (e.Handled || e.RemovedItems.Count < 1)
                return;

            // Get the last changed part and the new part
            // Old part is useful so that way we don't end up doing weird index updating shenanigans when the ComboBox updates
            string newPart = e.AddedItems.Cast<string>().FirstOrDefault();
            string oldPart = e.RemovedItems.Cast<string>().FirstOrDefault();

            if (newPart == default || oldPart == default)
                return;

            if (!list.Contains(newPart))
                return;

            Console.WriteLine($"Changed \"{oldPart}\" to \"{newPart}\"");
        }
        #endregion

        #region Profile
        private void ClearLLBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Profile == null)
                return;

            Profile.LostLootItems.Clear();
        }

        private void ClearBankBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Profile == null)
                return;

            Profile.BankItems.Clear();
        }

        #region Customization Unlockers/Lockers
        private void UnlockCustomizations_Click(object sender, RoutedEventArgs e)
        {
            List<string> customizations = new List<string>();
            customizations.AddRange(InventorySerialDatabase.OakCustomizationData.ToArray());

            foreach (string assetPath in customizations)
            {
                string lowerAsset = assetPath.ToLower();

                if (lowerAsset.Contains("default") || (lowerAsset.Contains("emote") && (lowerAsset.Contains("wave") || lowerAsset.Contains("cheer") || lowerAsset.Contains("laugh") || lowerAsset.Contains("point"))))
                    continue;

                if (!Profile.Profile.UnlockedCustomizations.Any(x => x.CustomizationAssetPath.Equals(assetPath)))
                {
                    OakSave.OakCustomizationSaveGameData d = new OakSave.OakCustomizationSaveGameData
                    {
                        CustomizationAssetPath = assetPath,
                        IsNew = true
                    };

                    Profile.Profile.UnlockedCustomizations.Add(d);

                    Console.WriteLine("Profile doesn't contain customization: {0}", assetPath);
                }
            }
        }

        private void LockCustomizations_Click(object sender, RoutedEventArgs e)
        {
            Profile.Profile.UnlockedCustomizations.Clear();
            Profile.Profile.UnlockedInventoryCustomizationParts.Clear();
        }
        #endregion
        #endregion

        #region About
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
        #endregion
        #endregion

        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args.Error == null)
            {
                if (args.IsUpdateAvailable)
                {
                    MessageBoxResult result;

                    if (args.Mandatory.Value)
                        result = MessageBox.Show($@"There is a new version {args.CurrentVersion} available. This update is required. Press OK to begin updating.", "Update Available", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        result = MessageBox.Show($@"There is a new version {args.CurrentVersion} available. You're using version {args.InstalledVersion}. Do you want to update now?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result.Equals(MessageBoxResult.Yes) || result.Equals(MessageBoxResult.OK))
                    {
                        try
                        {
#if !SINGLE_FILE
                            // Change what we're doing depending on whether or not we're built in "single file" (One executable in a ZIP) or "release" (Distributed as a zip with multiple files and folders)
                            args.DownloadURL = args.DownloadURL.Replace("-Portable", "");
#endif

#if !DEBUG
                            if (AutoUpdater.DownloadUpdate(args))
                                Application.Current.Shutdown();
#endif
                        }
                        catch (Exception exception)
                        {
                            MessageBox.Show(exception.Message, exception.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
            else
            {
                if (args.Error is WebException)
                    MessageBox.Show("There is a problem reaching update server. Please check your internet connection and try again later.", "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    MessageBox.Show(args.Error.Message, args.Error.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            AutoUpdater.Start(UpdateURL);
        }

        private bool ChangeReduxMode()
        {
            return MessageBox.Show("Changing the REDUX mode will restart the application.\nYOU WILL LOSE ANY UNSAVED PROGRESS!\nAre you sure you want to proceed?", "REDUX Mode Change", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        private void ReduxMode_Checked(object sender, RoutedEventArgs e)
        {
            if (Launched)
            {
                bool rmChecked = (bool)ReduxMode.IsChecked;

                // Get current Redux database setting
                bool bRedux = InventorySerialDatabase.bReduxDb;

                // If Redux mode has changed, store new bool and reset.
                if (bRedux != rmChecked)
                {
                    // Update Redux mode setting in application.
                    Properties.Settings.Default.bReduxModeEnabled = rmChecked;
                    Properties.Settings.Default.Save();

                    // Verify restart
                    if (ChangeReduxMode())
                    {
                        // Pass checked Redux mode bool value to WonderlandsTools setting
                        InventorySerialDatabase.setIsRedux(rmChecked);

                        // Restart the application to reinitialize database to proper mode
                        Process.Start(Process.GetCurrentProcess().MainModule.FileName);
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        // User canceled, revert CheckBox to previous state.
                        ReduxMode.IsChecked = !rmChecked;
                    }
                }
            }
        }

        private void ComboBox_KeyUp(object sender, KeyEventArgs e)
        {
            ComboBox s = (ComboBox)sender;
            CollectionView itemsViewOriginal = (CollectionView)CollectionViewSource.GetDefaultView(s.ItemsSource);

            itemsViewOriginal.Filter = ((o) =>
            {
                if (String.IsNullOrEmpty(s.Text)) return true;
                else
                {
                    if (((string)o).Contains(s.Text)) return true;
                    else return false;
                }
            });

            itemsViewOriginal.Refresh();
        }
    }
}