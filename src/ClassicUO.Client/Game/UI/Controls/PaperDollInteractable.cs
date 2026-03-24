// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using MonoGame.Extended.Particles;
using MonoGame.Extended.Particles.Data;
using MonoGame.Extended.Particles.Modifiers;
using Myra.Graphics2D.TextureAtlases;
using System;
using System.Collections.Generic;

namespace ClassicUO.Game.UI.Controls
{
    public class PaperDollInteractable : Control
    {
        private static readonly Layer[] _layerOrder =
        {
            Layer.Cloak,
            Layer.Shirt,
            Layer.Pants,
            Layer.Shoes,
            Layer.Legs,
            Layer.Arms,
            Layer.Torso,
            Layer.Tunic,
            Layer.Ring,
            Layer.Bracelet,
            Layer.Face,
            Layer.Gloves,
            Layer.Skirt,
            Layer.Robe,
            Layer.Waist,
            Layer.Necklace,
            Layer.Hair,
            Layer.Beard,
            Layer.Earrings,
            Layer.Helmet,
            Layer.OneHanded,
            Layer.TwoHanded,
            Layer.Talisman
        };

        private static readonly Layer[] _layerOrder_quiver_fix =
        {
            Layer.Shirt,
            Layer.Pants,
            Layer.Shoes,
            Layer.Legs,
            Layer.Arms,
            Layer.Torso,
            Layer.Tunic,
            Layer.Ring,
            Layer.Bracelet,
            Layer.Face,
            Layer.Gloves,
            Layer.Skirt,
            Layer.Robe,
            Layer.Cloak,
            Layer.Waist,
            Layer.Necklace,
            Layer.Hair,
            Layer.Beard,
            Layer.Earrings,
            Layer.Helmet,
            Layer.OneHanded,
            Layer.TwoHanded,
            Layer.Talisman
        };

        private static readonly Layer[] _layerOrder_parrot_fix =
        {
            Layer.Shirt,
            Layer.Pants,
            Layer.Shoes,
            Layer.Legs,
            Layer.Arms,
            Layer.Torso,
            Layer.Tunic,
            Layer.Cloak,
            Layer.Ring,
            Layer.Bracelet,
            Layer.Face,
            Layer.Gloves,
            Layer.Skirt,
            Layer.Waist,
            Layer.Necklace,
            Layer.Hair,
            Layer.Beard,
            Layer.Earrings,
            Layer.Helmet,
            Layer.OneHanded,
            Layer.TwoHanded,
            Layer.Talisman,
            Layer.Robe
        };

        private readonly PaperDollGump _paperDollGump;
        private Texture2D EffectTexture = null;
        private Texture2D ParticleTexture = null;
        private ParticleEffect Effect = null;

        private bool _updateUI;

        public PaperDollInteractable(int x, int y, uint serial, PaperDollGump paperDollGump, double scale = 1f)
        {
            X = x;
            Y = y;
            _paperDollGump = paperDollGump;
            AcceptMouseInput = false;
            LocalSerial = serial;
            _updateUI = true;

            // Only set Scale/InternalScale for non-ScalableGump parents
            // ScalableGump.Add() will handle scaling automatically
            Scale = InternalScale = scale;

            if (EffectTexture == null)
                PNGLoader.Instance.TryGetEmbeddedTexture("effect1.png", out EffectTexture);
            if (ParticleTexture == null)
                PNGLoader.Instance.TryGetEmbeddedTexture("circle.png", out ParticleTexture);

            if (Effect == null)
            {
                var emitter = new ParticleEmitter(
                    textureRegion: new TextureRegion2D(ParticleTexture),
                    capacity: 500,
                    profile: Profile.Circle(10, Profile.CircleRadiation.Out));

                emitter.Parameters = new ParticleReleaseParameters
                {
                    Quantity = 5,
                    Speed = new Range<float>(50, 100),
                    Rotation = new Range<float>(0, MathF.PI * 2),
                    Scale = new Range<float>(0.5f, 1f),
                    Color = new Range<Color>(Color.Orange, Color.Yellow),
                    Life = new Range<float>(0.5f, 1.5f)
                };

                emitter.Modifiers.Add(new LinearGravityModifier(new Vector2(0, 30)));

                _particleEffect = new ParticleEffect();
                _particleEffect.Emitters.Add(emitter);
            }
        }

        public bool HasFakeItem { get; private set; }

        public override void Update()
        {
            base.Update();

            if (_updateUI)
            {
                UpdateUI();

                _updateUI = false;
            }
        }

        public void SetFakeItem(bool value)
        {
            // Only trigger update when ENABLING fake item, not when disabling
            // Disabling should be followed by an explicit RequestUpdate() call
            _updateUI = !HasFakeItem && value;
            HasFakeItem = value;
        }

        private void UpdateUI()
        {
            if (IsDisposed)
            {
                return;
            }

            Mobile mobile = World.Instance.Mobiles.Get(LocalSerial);

            if (mobile == null || mobile.IsDestroyed)
            {
                Dispose();

                return;
            }

            Clear();

            // Add the base gump - the semi-naked paper doll.
            ushort body;
            ushort hue = mobile.Hue;

            if (mobile.Graphic == 0x0191 || mobile.Graphic == 0x0193)
            {
                body = 0x000D;
            }
            else if (mobile.Graphic == 0x025D)
            {
                body = 0x000E;
            }
            else if (mobile.Graphic == 0x025E)
            {
                body = 0x000F;
            }
            else if (mobile.Graphic == 0x029A || mobile.Graphic == 0x02B6)
            {
                body = 0x029A;
            }
            else if (mobile.Graphic == 0x029B || mobile.Graphic == 0x02B7)
            {
                body = 0x0299;
            }
            else if (mobile.Graphic == 0x04E5)
            {
                body = 0xC835;
            }
            else if (mobile.Graphic == 0x03DB)
            {
                body = 0x000C;
                hue = 0x03EA;
            }
            else if (mobile.IsFemale)
            {
                body = 0x000D;
            }
            else
            {
                body = 0x000C;
            }

            AddBackgroundEffect();

            // body
            Add(new GumpPic(0, 0, body, hue) { IsPartialHue = true }.ScaleWidthAndHeight(Scale).SetInternalScale(Scale));

            if (mobile.Graphic == 0x03DB)
            {
                Add(
                    new GumpPic(0, 0, 0xC72B, mobile.Hue)
                    {
                        AcceptMouseInput = true,
                        IsPartialHue = true
                    }.ScaleWidthAndHeight(Scale).SetInternalScale(Scale)
                );
            }

            // equipment
            Item equipItem = mobile.FindItemByLayer(Layer.Cloak);
            Item arms = mobile.FindItemByLayer(Layer.Arms);
            Item robe = mobile.FindItemByLayer(Layer.Robe);

            bool switch_arms_with_torso = false;

            if (arms != null)
            {
                switch_arms_with_torso = arms.Graphic == 0x1410 || arms.Graphic == 0x1417;
            }
            else if (
                HasFakeItem
                && Client.Game.UO.GameCursor.ItemHold.Enabled
                && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
                && (byte)Layer.Arms == Client.Game.UO.GameCursor.ItemHold.ItemData.Layer
            )
            {
                switch_arms_with_torso =
                    Client.Game.UO.GameCursor.ItemHold.Graphic == 0x1410
                    || Client.Game.UO.GameCursor.ItemHold.Graphic == 0x1417;
            }

            Layer[] layers;

            if (equipItem != null)
            {
                if (robe != null && (robe.Graphic == 0xA2CB || robe.Graphic == 0xA2CA)) // parrot
                {
                    layers = _layerOrder_parrot_fix;
                }
                else
                {
                    layers = equipItem.ItemData.IsContainer ? _layerOrder_quiver_fix : _layerOrder;

                    if (Settings.GlobalSettings.CustomServer == Settings.CustomServers.Eventine)
                        layers = equipItem.ItemData.IsContainer ? _layerOrder_quiver_fix : equipItem.Graphic == 0xA413 ? _layerOrder_quiver_fix : _layerOrder;
                }
            }
            else if (
                HasFakeItem
                && Client.Game.UO.GameCursor.ItemHold.Enabled
                && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
                && (byte)Layer.Cloak == Client.Game.UO.GameCursor.ItemHold.ItemData.Layer
            )
            {
                layers = Client.Game.UO.GameCursor.ItemHold.ItemData.IsContainer
                    ? _layerOrder_quiver_fix
                    : _layerOrder;
            }
            else
            {
                layers = _layerOrder;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];

                if (switch_arms_with_torso)
                {
                    if (layer == Layer.Arms)
                    {
                        layer = Layer.Torso;
                    }
                    else if (layer == Layer.Torso)
                    {
                        layer = Layer.Arms;
                    }
                }

                equipItem = mobile.FindItemByLayer(layer);

                if (equipItem != null)
                {
                    if (Mobile.IsCovered(mobile, layer))
                    {
                        continue;
                    }

                    ushort id = GetAnimID(
                        mobile.Graphic,
                        equipItem.Graphic,
                        equipItem.ItemData.AnimID,
                        mobile.IsFemale
                    );
                    Add(
                        new GumpPicEquipment(
                            _paperDollGump,
                            equipItem.Serial,
                            0,
                            0,
                            id,
                            (ushort)(equipItem.Hue & 0x3FFF),
                            layer
                        )
                        {
                            AcceptMouseInput = true,
                            IsPartialHue = equipItem.ItemData.IsPartialHue,
                            CanLift =
                                World.Instance.InGame
                                && !World.Instance.Player.IsDead
                                && layer != Layer.Beard
                                && layer != Layer.Hair
                                && ((_paperDollGump != null && _paperDollGump.CanLift) || (_paperDollGump != null && LocalSerial == _paperDollGump.World.Player)),
                        }.ScaleWidthAndHeight(Scale).SetInternalScale(InternalScale)
                    );
                }
                else if (
                    HasFakeItem
                    && Client.Game.UO.GameCursor.ItemHold.Enabled
                    && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
                    && (byte)layer == Client.Game.UO.GameCursor.ItemHold.ItemData.Layer
                    && Client.Game.UO.GameCursor.ItemHold.ItemData.AnimID != 0
                )
                {
                    ushort id = GetAnimID(
                        mobile.Graphic,
                        Client.Game.UO.GameCursor.ItemHold.Graphic,
                        Client.Game.UO.GameCursor.ItemHold.ItemData.AnimID,
                        mobile.IsFemale
                    );

                    Add(
                        new GumpPicEquipment(
                            _paperDollGump,
                            0,
                            0,
                            0,
                            id,
                            (ushort)(Client.Game.UO.GameCursor.ItemHold.Hue & 0x3FFF),
                            Client.Game.UO.GameCursor.ItemHold.Layer
                        )
                        {
                            AcceptMouseInput = true,
                            IsPartialHue = Client.Game.UO.GameCursor.ItemHold.IsPartialHue,
                            Alpha = 0.5f
                        }.ScaleWidthAndHeight(Scale).SetInternalScale(InternalScale)
                    );
                }
            }

            equipItem = mobile.Backpack;

            if (equipItem != null && equipItem.ItemData.AnimID != 0 && _paperDollGump != null)
            {
                ushort backpackGraphic = (ushort)(
                    equipItem.ItemData.AnimID + Constants.MALE_GUMP_OFFSET
                );

                // If player, apply backpack skin
                if (mobile.Serial == _paperDollGump.World.Player.Serial)
                {
                    Renderer.Gumps.Gump gump = Client.Game.UO.Gumps;

                    switch (ProfileManager.CurrentProfile.BackpackStyle)
                    {
                        case 1:
                            if (gump.GetGump(0x777B).Texture != null)
                            {
                                backpackGraphic = 0x777B; // Suede Backpack
                            }

                            break;
                        case 2:
                            if (gump.GetGump(0x777C).Texture != null)
                            {
                                backpackGraphic = 0x777C; // Polar Bear Backpack
                            }

                            break;
                        case 3:
                            if (gump.GetGump(0x777D).Texture != null)
                            {
                                backpackGraphic = 0x777D; // Ghoul Skin Backpack
                            }

                            break;
                        default:
                            if (gump.GetGump(0xC4F6).Texture != null)
                            {
                                backpackGraphic = 0xC4F6; // Default Backpack
                            }

                            break;
                    }
                }

                int bx = 0;

                if (_paperDollGump.World.ClientFeatures.PaperdollBooks)
                {
                    bx = 6;
                }

                Add(
                    new GumpPicEquipment(
                        _paperDollGump,
                        equipItem.Serial,
                        -bx,
                        0,
                        backpackGraphic,
                        (ushort)(equipItem.Hue & 0x3FFF),
                        Layer.Backpack
                    )
                    {
                        AcceptMouseInput = true
                    }.ScaleWidthAndHeight(Scale).SetInternalScale(Scale)
                );
            }
        }

        public void RequestUpdate() => _updateUI = true;

        protected static ushort GetAnimID(ushort mobileGraphic, ushort itemGraphic, ushort animID, bool isfemale)
        {
            int offset = isfemale ? Constants.FEMALE_GUMP_OFFSET : Constants.MALE_GUMP_OFFSET;

            if (
                    Client.Game.UO.Version >= ClientVersion.CV_7000
                    && animID == 0x03CA // graphic for dead shroud
                    && (mobileGraphic == 0x02B7 || mobileGraphic == 0x02B6)
                ) // dead gargoyle graphics
            {
                animID = 0x0223;
            }

            Client.Game.UO.Animations.ConvertBodyIfNeeded(ref mobileGraphic);

            if (
                Client.Game.UO.FileManager.Animations.EquipConversions.TryGetValue(
                    mobileGraphic,
                    out Dictionary<ushort, EquipConvData> dict
                )
            )
            {
                if (dict.TryGetValue(animID, out EquipConvData data))
                {
                    if (data.Gump > Constants.MALE_GUMP_OFFSET)
                    {
                        animID = (ushort)(
                            data.Gump >= Constants.FEMALE_GUMP_OFFSET
                                ? data.Gump - Constants.FEMALE_GUMP_OFFSET
                                : data.Gump - Constants.MALE_GUMP_OFFSET
                        );
                    }
                    else
                    {
                        animID = data.Gump;
                    }
                }
            }

            if (Client.Game.UO.FileManager.TileArt.TryGetTileArtInfo(itemGraphic, out TileArtInfo tileArtInfo))
            {
                if (tileArtInfo.TryGetAppearance(mobileGraphic, out uint appareanceId))
                {
                    ushort gumpId = (ushort)(Constants.MALE_GUMP_OFFSET + appareanceId);
                    if (Client.Game.UO.Gumps.GetGump(gumpId).Texture != null)
                    {
                        Log.Info($"Equip conversion through tileart.uop done: old {animID} -> new {appareanceId}");
                        return gumpId;
                    }
                }
            }

            _ = IsAnimExistsInGump(animID, ref offset, isfemale);

            return (ushort)(animID + offset);
        }

        private static bool IsAnimExistsInGump(ushort animID, ref int offset, bool isFemale)
        {
            int requested = animID + offset;
            if (
                    requested > GumpsLoader.MAX_GUMP_DATA_INDEX_COUNT
                    || Client.Game.UO.Gumps.GetGump((ushort)(requested)).Texture == null
                )
            {
                // inverse
                offset = isFemale ? Constants.MALE_GUMP_OFFSET : Constants.FEMALE_GUMP_OFFSET;
                requested = animID + offset;
            }

            if (Client.Game.UO.Gumps.GetGump((ushort)(requested)).Texture == null)
            {
                return false;
            }

            return true;
        }

        private void AddBackgroundEffect()
        {
            if (EffectTexture == null)
                return;

            var pic = new AnimatedEmbeddedGumpPic(0, 0, EffectTexture, 5, 3, 100);
            Add(pic);

        }

        protected class GumpPicEquipment : GumpPic
        {
            private readonly Layer _layer;
            private readonly Gump _gump;

            public GumpPicEquipment(
                Gump gump,
                uint serial,
                int x,
                int y,
                ushort graphic,
                ushort hue,
                Layer layer
            ) : base(x, y, graphic, hue)
            {
                _gump = gump;
                LocalSerial = serial;
                CanMove = false;
                _layer = layer;

                if (SerialHelper.IsValid(serial) && _gump?.World?.InGame == true)
                {
                    SetTooltip(serial);
                }
            }

            public bool CanLift { get; set; }

            public override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
            {
                if (button != MouseButtonType.Left)
                {
                    return false;
                }

                // this check is necessary to avoid crashes during character creation
                if (_gump?.World?.InGame == true)
                {
                    GameActions.DoubleClick(_gump.World, LocalSerial);
                }

                return true;
            }

            public override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                SelectedObject.Object = _gump?.World?.Get(LocalSerial);
                base.OnMouseUp(x, y, button);
            }

            public override void Update()
            {
                base.Update();

                if (_gump?.World?.InGame == true)
                {
                    if (
                        CanLift
                        && !Client.Game.UO.GameCursor.ItemHold.Enabled
                        && Mouse.LButtonPressed
                        && UIManager.LastControlMouseDown(MouseButtonType.Left) == this
                        && (
                            Mouse.LastLeftButtonClickTime != 0xFFFF_FFFF
                                && Mouse.LastLeftButtonClickTime != 0
                                && Mouse.LastLeftButtonClickTime + Mouse.MOUSE_DELAY_DOUBLE_CLICK
                                    < Time.Ticks
                            || Mouse.LDragOffset != Point.Zero
                        )
                    )
                    {
                        GameActions.PickUp(_gump.World, LocalSerial, 0, 0);

                        if (_layer == Layer.OneHanded || _layer == Layer.TwoHanded)
                        {
                            _gump.World.Player.UpdateAbilities();
                        }
                    }
                    else if (MouseIsOver)
                    {
                        SelectedObject.Object = _gump?.World?.Get(LocalSerial);
                    }
                }
            }

            public override void OnMouseOver(int x, int y) => SelectedObject.Object = _gump?.World?.Get(LocalSerial);
        }

        protected class AnimatedEmbeddedGumpPic : GumpPicBase
        {
            private readonly Texture2D _spriteSheet;
            private readonly int _columns;
            private readonly int _rows;
            private readonly int _frameWidth;
            private readonly int _frameHeight;
            private readonly int _totalFrames;
            private readonly uint _frameDelayMs;
            private ulong _nextFrameTime;
            private int _currentFrame;

            public AnimatedEmbeddedGumpPic(int x, int y, Texture2D spriteSheet, int columns, int rows, uint frameDelayMs)
            {
                X = x;
                Y = y;
                _spriteSheet = spriteSheet;
                _columns = columns;
                _rows = rows;
                _frameDelayMs = frameDelayMs;
                _totalFrames = columns * rows;
                _currentFrame = 0;
                _nextFrameTime = Time.Ticks + _frameDelayMs;

                if (_spriteSheet != null)
                {
                    _frameWidth = _spriteSheet.Width / columns;
                    _frameHeight = _spriteSheet.Height / rows;
                    Width = _frameWidth;
                    Height = _frameHeight;
                }

                AcceptMouseInput = false;
            }

            public override void Update()
            {
                base.Update();

                if (Time.Ticks >= _nextFrameTime)
                {
                    _currentFrame = (_currentFrame + 1) % _totalFrames;
                    _nextFrameTime = Time.Ticks + _frameDelayMs;
                }
            }

            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                if (IsDisposed || _spriteSheet == null || _spriteSheet.IsDisposed)
                {
                    return false;
                }

                int column = _currentFrame % _columns;
                int row = _currentFrame / _columns;

                var sourceRect = new Rectangle(
                    column * _frameWidth,
                    row * _frameHeight,
                    _frameWidth,
                    _frameHeight
                );

                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);

                batcher.Draw(
                    _spriteSheet,
                    new Rectangle(x, y, _frameWidth, _frameHeight),
                    sourceRect,
                    hueVector
                );

                return base.Draw(batcher, x, y);
            }
        }

        protected class TestParticle : Control
        {
            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                return base.Draw(batcher, x, y);
            }
        }
    }
}
