﻿using System.IO;
using System.Reflection;
using UnityEngine;

namespace DarwinAwards;

public static class Helper
{
	private static byte[] ReadEmbeddedFileBytes(string name)
	{
		using MemoryStream stream = new();
		Assembly.GetExecutingAssembly().GetManifestResourceStream("DarwinAwards." + name)?.CopyTo(stream);
		return stream.ToArray();
	}

	public static Texture2D loadTexture(string name)
	{
		Texture2D texture = new(0, 0);
		texture.LoadImage(ReadEmbeddedFileBytes("icons." + name));
		return texture;
	}

	public static Sprite loadSprite(string name, int width = 64, int height = 64) => Sprite.Create(loadTexture(name), new Rect(0, 0, width, height), Vector2.zero);
}
