﻿using RomUtilities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using static System.Math;

namespace FFMQLib
{
	public enum MapObjectType : byte
	{
		Talk = 0x00,
		Battle = 0x01,
		Chest = 0x02
	}
	public enum EnemiesDensity : int
	{
		[Description("100%")]
		All = 0,
		[Description("75%")]
		ThreeQuarter,
		[Description("50%")]
		Half,
		[Description("25%")]
		Quarter,
		[Description("0%")]
		None,
	}

	public partial class FFMQRom : SnesRom
	{

		public class ObjectList
		{
			private List<List<MapObject>> _collections = new List<List<MapObject>>();
			private List<int> _pointerCollectionPairs = new();
			private List<Blob> _areaattributes = new();
			public List<(int, int, int)> ChestList = new();
			private List<Blob> _attributepointers = new();

			public ObjectList(FFMQRom rom)
			{
				_attributepointers = rom.Get(RomOffsets.AreaAttributesPointers, RomOffsets.AreaAttributesPointersQty * 2).Chunk(2);

				var previousPointer = _attributepointers[0].ToUShorts()[0];
				var collectionCount = 0;

				for (int i = 0; i < _attributepointers.Count; i++)
				{
					if (i != 0 && _attributepointers[i].ToUShorts()[0] == previousPointer)
					{
						_pointerCollectionPairs.Add(collectionCount-1);
						continue;
					}

					_pointerCollectionPairs.Add(collectionCount);

					var address = RomOffsets.AreaAttributesPointersBase + _attributepointers[i].ToUShorts()[0];
					_areaattributes.Add(rom.Get(address, 8));

					address += 8;

					_collections.Add(new List<MapObject>());

					while (rom.Data[address] != 0xFF)
					{
						_collections.Last().Add(new MapObject(address, rom));
						address += RomOffsets.MapObjectsAttributesSize;
					}
					collectionCount++;
					previousPointer = _attributepointers[i].ToUShorts()[0];
				}

				for(int  i = 0; i < _collections.Count; i++)
				{
					for(int j = 0; j < _collections[i].Count; j++)
					{
						if (_collections[i][j].Type == MapObjectType.Chest)
						{
							ChestList.Add((_collections[i][j].Value, i, j));
						}
					}
				}
			}
			public void UpdateChests(Flags flags, ItemsPlacement itemsPlacement)
			{
				// Update Tristam Elixir Chest so it's taken into account
				_collections[_pointerCollectionPairs[0x16]][0x05].Type = MapObjectType.Chest;
				_collections[_pointerCollectionPairs[0x16]][0x05].Value = 0x04;
				_collections[_pointerCollectionPairs[0x16]][0x05].Gameflag = 0xAD;

				ChestList.Add((0x04, _pointerCollectionPairs[0x16], 0x05));

				List<Items> boxItems = new() { Items.Potion, Items.HealPotion, Items.Refresher, Items.Seed, Items.BombRefill, Items.ProjectileRefill };

				var test = itemsPlacement.ItemsLocations.Where(x => x.Type == TreasureType.Chest).ToList();

				foreach (var item in itemsPlacement.ItemsLocations.Where(x => x.Type == TreasureType.Chest || x.Type == TreasureType.Box).OrderBy(x => x.ObjectId))
				{
					byte sprite = 0x26;
					
					if (item.Type == TreasureType.Chest)
					{
						sprite = 0x24;
					}
					
					List<(int,int,int)> targetChest = ChestList.Where(x => x.Item1 == item.ObjectId).ToList();
					
					if (!targetChest.Any()) continue;

					_collections[targetChest.First().Item2][targetChest.First().Item3].Sprite = sprite;
					_collections[targetChest.First().Item2][targetChest.First().Item3].Gameflag = 0x00;
				}

				// Copy box+chest from Level Forest 2nd map to 1st map
				for (int i = 0; i < 5; i++)
				{
					_collections[0x09][0x0C + i].RawOverwrite(_collections[0x0A][0x0C + i].RawArray());
				}
			}
			public int GetAreaMapId(int area)
			{
				return _areaattributes[area][1];
			}
			public void SetEnemiesDensity(Flags flags, MT19337 rng)
			{
				int density = 100;

				switch (flags.EnemiesDensity)
				{
					case EnemiesDensity.All: return;
					case EnemiesDensity.ThreeQuarter: density = 75; break;
					case EnemiesDensity.Half: density = 50; break;
					case EnemiesDensity.Quarter: density = 25; break;
					case EnemiesDensity.None: density = 0; break;
				}

				for (int i = 0; i < _collections.Count; i++)
				{
					var enemiescollection = _collections[i].Where(x => x.Type == MapObjectType.Battle).ToList();
					int totalcount = enemiescollection.Count;
					int toremove = ((100 - density) * totalcount) / 100;

					for (int j = 0; j < toremove; j++)
					{
						rng.TakeFrom(enemiescollection).Gameflag = 0xFE;
					}
				}
			}
			public void ShuffleEnemiesPosition(GameMaps maps, Flags flags, MT19337 rng)
			{
				Dictionary<MapList, List<(int, int)>> excludedCoordinates = new Dictionary<MapList, List<(int, int)>> {
					{ MapList.LevelAliveForest, new List<(int, int)> { (0x34, 0x10), (0x34, 0x0E) } },
					{ MapList.MineExterior, new List<(int, int)> { (0x0B, 0x026), (0x23, 0x2A), (0x21, 0x0F), (0x3B, 0x30) } },
					{ MapList.VolcanoBase, new List<(int, int)> { (0x0F, 0x08) } },
					{ MapList.MountGale, new List<(int, int)> { (0x18, 0x16), (0x17, 0x16), (0x1C, 0x14), (0x32, 0x23), (0x1F, 0x12), (0x27, 0x0F), (0x27, 0x0E), (0x22, 0x0A) } },
					{ MapList.MacShipDeck, new List<(int, int)> { (0x13, 0x1D) } },
					{ MapList.MacShipInterior, new List<(int, int)> { (0x12, 0x11), (0x08, 0x08), (0x11, 0x21) } },
					{ MapList.IcePyramidA, new List<(int, int)> { (0x21, 0x3D), (0x2A, 0x3C), (0x25, 0x34) } },
					{ MapList.LavaDomeInteriorA, new List<(int, int)> { (0x35, 0x14), (0x33, 0x14), (0x36, 0x0A), (0x36, 0x08), (0x34, 0x08), (0x32, 0x08), (0x29, 0x1E), (0x27, 0x1E), (0x29, 0x20), (0x29, 0x22), (0x2B, 0x22), (0x29, 0x24), (0x2B, 0x24), (0x29, 0x26), (0x19, 0x36), (0x03, 0x34), (0x05, 0x2C), (0x02, 0x23), (0x0E, 0x24), (0x28, 0x04) } },
					{ MapList.LavaDomeInteriorB, new List<(int, int)> { (0x04, 0x07), (0x0B, 0x12), (0x07, 0x14), (0x09, 0x19), (0x13, 0x14), (0x11, 0x14), (0x39, 0x05), (0x25, 0x0C), (0x23, 0x12), (0x25, 0x12), (0x2A, 0x0C), (0x2C, 0x0C), (0x38, 0x11), (0x36, 0x11), (0x2E, 0x16), (0x30, 0x16) } },
					{ MapList.PazuzuTowerA, new List<(int, int)> { (0x14, 0x0A), (0x10, 0x12), (0x10, 0x13), (0x0F, 0x16), (0x0F, 0x17), (0x12, 0x31), (0x14, 0x31), (0x2C, 0x33), (0x2A, 0x33), (0x32, 0x33), (0x34, 0x33) } },
					{ MapList.PazuzuTowerB, new List<(int, int)> { (0x2D, 0x19), (0x10, 0x2D), (0x06, 0x2E), (0x07, 0x32) } },
					{ MapList.FocusTowerBase, new List<(int, int)> { (0x2C, 0x1A), (0x25, 0x13), (0x1F, 0x13), (0x1E, 0x13), (0x1C, 0x1E), (0x1B, 0x1E), (0x1A, 0x1E), (0x19, 0x1E), (0x18, 0x1E), (0x0D, 0x12), (0x0E, 0x12), (0x0F, 0x12), (0x13, 0x13), (0x13, 0x15), (0x13, 0x17) } },
					{ MapList.DoomCastleIce, new List<(int, int)> { (0x1C, 0x24), (0x20, 0x24), (0x1C, 0x20), (0x25, 0x1A), (0x24, 0x19), (0x0F, 0x26), (0x1B, 0x0F), (0x1E, 0x16), (0x11, 0x1A), (0x0A, 0x17), (0x0B, 0x16), (0x18, 0x0B), (0x07, 0x0B) } },
					{ MapList.DoomCastleLava, new List<(int, int)> { (0x25, 0x1C), (0x25, 0x1F), (0x25, 0x21), (0x22, 0x17), (0x1B, 0x16), (0x1A, 0x1F), (0x13, 0x1F), (0x11, 0x1F), (0x10, 0x09), (0x11, 0x09), (0x0E, 0x09), (0x12, 0x11), (0x14, 0x18), (0x16, 0x18), (0x18, 0x0B), (0x0B, 0x20), (0x0B, 0x22) } },
					{ MapList.DoomCastleSky, new List<(int, int)> { (0x19, 0x22), (0x17, 0x22) } },
				};

				Dictionary<MapList, List<byte>> excludedTiles = new Dictionary<MapList, List<byte>> {
					{ MapList.GiantTreeA, new List<byte> { 0x06, 0x16 } },
					{ MapList.GiantTreeB, new List<byte> { 0x06, 0x16 } },
					{ MapList.LavaDomeExterior, new List<byte> { 0x7E, 0xFE, 0x37, 0x41 } },
					{ MapList.LavaDomeInteriorA, new List<byte> { 0x7E, 0xFE, 0x37, 0x41 } },
					{ MapList.LavaDomeInteriorB, new List<byte> { 0x49, 0x4B, 0x7D } },
					{ MapList.DoomCastleLava, new List<byte> { 0x57, 0x41 } },
					{ MapList.DoomCastleSky, new List<byte> { 0x0F } },
				};

				List<int> hookMaps = new() { (int)MapList.GiantTreeA, (int)MapList.GiantTreeB, (int)MapList.MountGale, (int)MapList.MacShipInterior, (int)MapList.PazuzuTowerA, (int)MapList.PazuzuTowerB, (int)MapList.FocusTowerBase, (int)MapList.DoomCastleIce, (int)MapList.DoomCastleLava };

				if (flags.ShuffleEnemiesPosition == false)
				{
					return;
				}

				for (int i = 0; i < _collections.Count; i++)
				{
					
					var enemiescollection = _collections[i].Where(x => x.Type == MapObjectType.Battle).ToList();
					if (!enemiescollection.Any())
					{
						continue;
					}
					int minx = Max(0, enemiescollection.Select(x => x.X).Min() - 1);
					int maxx = enemiescollection.Select(x => x.X).Max() + 1;
					int miny = Max(0, enemiescollection.Select(x => x.Y).Min() - 1);
					int maxy = enemiescollection.Select(x => x.Y).Max() + 1;

					var validLayers = enemiescollection.Select(x => x.Layer).Distinct().ToList();
					var targetmap = GetAreaMapId(i);

					var currentExcludedTiles = excludedTiles.ContainsKey((MapList)targetmap) ? excludedTiles[(MapList)targetmap] : new List<byte>();
					var currentExcludedCoordinates = excludedCoordinates.ContainsKey((MapList)targetmap) ? excludedCoordinates[(MapList)targetmap] : new List<(int,int)>();

					List <(byte, byte)> selectedPositions = _collections[i].Where(x => x.Type != MapObjectType.Battle).Select(x => (x.X, x.Y)).ToList();

					if (hookMaps.Contains(targetmap)) // Creat an exclusion zone around hooks
					{
						var hookList = _collections[i].Where(x => x.Sprite == 0x28).ToList();
						foreach (var hook in hookList)
						{
							for (int j = Max(hook.X - 5, miny); j <= Min(hook.X + 5, maxx); j++)
							{
								selectedPositions.Add(((byte)j, (byte)hook.Y));
							}

							for (int j = Max(hook.Y - 5, miny); j <= Min(hook.Y + 5, maxy); j++)
							{
								selectedPositions.Add(((byte)hook.X, (byte)j));
							}
						}
					}

					// Worm Party
					if (i == _pointerCollectionPairs[0x48] && rng.Between(1,20) == 10)
					{
						validLayers = new() { 0x02 };
					}

					foreach (var enemy in enemiescollection)
					{
						bool placed = false;
						while (!placed)
						{
							byte newx = (byte)rng.Between(minx, maxx);
							byte newy = (byte)rng.Between(miny, maxy);
							if (!selectedPositions.Contains((newx, newy))
								&& !currentExcludedCoordinates.Contains((newx, newy))
								&& validLayers.Contains(maps[targetmap].WalkableByte((int)newx, (int)newy))
								&& !maps[targetmap].IsScriptTile((int)newx, (int)newy)
								&& !currentExcludedTiles.Contains(maps[targetmap].TileValue((int)newx, (int)newy)))
							{
								selectedPositions.Add((newx, newy));
								enemy.X = newx;
								enemy.Y = newy;
								enemy.Layer = maps[targetmap].WalkableByte((int)newx, (int)newy);
								placed = true;
							}
						}
					}
				}
			}
			public void WriteAll(FFMQRom rom)
			{
				var pointerOffset = _attributepointers[0].ToUShorts()[0];
				var pointersCount = 0;

				for (int i = 0; i < _collections.Count; i++)
				{
					var pointersQty = _pointerCollectionPairs.Where(x => x == i).Count();

					for (int j = 0; j < pointersQty; j++)
					{
						_attributepointers[pointersCount + j] = new byte[] { (byte)(pointerOffset % 0x100), (byte)(pointerOffset / 0x100) };
					}
					pointersCount += pointersQty;					
					
					rom.Put(RomOffsets.AreaAttributesPointersBase + pointerOffset, _areaattributes[i]);

					pointerOffset += 8;

					for (int j = 0; j < _collections[i].Count; j++)
					{
						_collections[i][j].WriteAt(rom, RomOffsets.AreaAttributesPointersBase + pointerOffset);
						pointerOffset += RomOffsets.MapObjectsAttributesSize;
					}

					rom.Put(RomOffsets.AreaAttributesPointersBase + pointerOffset, Blob.FromHex("FF"));
					pointerOffset++;
				}

				rom.Put(RomOffsets.AreaAttributesPointers, _attributepointers.SelectMany(x => x.ToBytes()).ToArray());
			}
			public List<MapObject> this[int floorid]
			{
				get => _collections[_pointerCollectionPairs[floorid]];
				set => _collections[_pointerCollectionPairs[floorid]] = value;
			}
		}

		public class MapObject
		{
			private List<byte> _array = new();
			private int _address;

			public MapObjectType Type { get; set; }
			public byte Gameflag { get; set; }
			public byte Value { get; set; }
			public bool Solid { get; set; }
			public bool Pushable { get; set; }
			public byte X { get; set; }
			public byte Y { get; set; }
			public byte Sprite { get; set; }
			public byte UnknownIndex { get; set; }
			public byte Orientation { get; set; }
			public byte Palette { get; set; }
			public byte Behavior { get; set; }
			public byte Layer { get; set; }

			public MapObject(int address, FFMQRom rom)
			{
				_address = address;
				_array = rom.Get(address, RomOffsets.MapObjectsAttributesSize).ToBytes().ToList();

				UpdateValues();
			}
			public void RawOverwrite(byte[] rawArray)
			{
				_array = rawArray.ToList();

				UpdateValues();
			}
			private void UpdateArray()
			{
				_array[0] = Gameflag;
				_array[1] = Value;
				_array[2] = (byte)((UnknownIndex * 64) + (Y & 0b0011_1111));
				_array[3] = (byte)((Orientation * 64) + (X & 0b0011_1111));
				_array[4] = (byte)(Palette * 16 + Behavior);
				_array[5] = (byte)(((int)Type * 8) + (Solid ? 0 : 0b0010_0000) + (Pushable ? 0b0100_0000 : 0) + (Layer & 0b0000_0111) + (_array[5] & 0b1000_0000));
				_array[6] = (byte)((int)Sprite + (_array[6] & 0b1000_0000));
			}

			private void UpdateValues()
			{
				Gameflag = _array[0];
				Value = _array[1];
				Y = (byte)(_array[2] & 0b0011_1111);
				UnknownIndex = (byte)((_array[2] & 0b1100_0000) / 64);
				X = (byte)(_array[3] & 0b0011_1111);
				Orientation = (byte)((_array[3] & 0b1100_0000) / 64);
				Palette = (byte)((_array[4] & 0b1111_0000) / 16);
				Behavior = (byte)((_array[4] & 0b0000_1111));
				Type = (MapObjectType)((_array[5] & 0b0001_1000) / 8);
				Solid = (bool)((_array[5] & 0b0010_0000) == 0);
				Pushable = (bool)((_array[5] & 0b0100_0000) != 0);
				Layer = (byte)(_array[5] & 0b0000_0111);
				Sprite = (byte)(_array[6] & 0b0111_1111);
			}
			public void Write(FFMQRom rom)
			{

				UpdateArray();

				rom.Put(_address, _array.ToArray());
			}
			public void WriteAt(FFMQRom rom, int address)
			{
				UpdateArray();

				rom.Put(address, _array.ToArray());
			}
			public bool IsNullEntry()
			{
				if (_array[0] == 0xFF)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			public byte[] RawArray()
			{
				UpdateArray();
				return _array.ToArray();
			}
			public string RawArrayString()
			{
				string HexAlphabet = "0123456789ABCDEF";

				var newstring = "";
				foreach (var hexvalue in _array)
				{
					newstring += HexAlphabet[hexvalue >> 4];
					newstring += HexAlphabet[hexvalue & 0x0F];
				}

				return newstring;
			}
		}

		public class Chest
		{
			public int ObjectId { get; set; }
			public int AreaId { get; set; }
			public byte AccessRequirements { get; set; }
			public byte TreasureId { get; set; }
			public Items Treasure { get; set; }
			public byte X { get; set; }
			public byte Y { get; set; }

			public Chest(int objid, int areaid, byte access, byte x, byte y, byte treasureid, Items treasure)
			{
				ObjectId = objid;
				AreaId = areaid;
				AccessRequirements = access;
				TreasureId = treasureid;
				X = x;
				Y = y;
				Treasure = treasure;
			}
		}
	}
}
