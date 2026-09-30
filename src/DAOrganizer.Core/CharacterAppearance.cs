namespace DAOrganizer.Core;

public sealed record CharacterAppearance(
    ushort HeadSprite, byte FaceShape, byte BodySprite, byte HairColor, byte SkinColor,
    ushort ArmsSprite, ushort ArmorSprite, ushort OvercoatSprite,
    ushort Accessory1Sprite, byte Accessory1Color,
    ushort Accessory2Sprite, byte Accessory2Color,
    ushort Accessory3Sprite, byte Accessory3Color);
