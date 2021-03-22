using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Barmetler
{
	[System.Serializable]
	public class TwoDimensionalArray<T>
	{
		public TwoDimensionalArray(int width, int height)
		{
			array = new T[width * height];
			this.width = width;
			this.height = height;
		}

		[SerializeField]
		private int width;

		[SerializeField]
		private int height;

		[SerializeField]
		private T[] array;

		public int Width { get => width; }
		public int Height { get => height; }

		public TwoDimensionalArray<T> Clone()
		{
			return new TwoDimensionalArray<T>(Width, height)
			{
				array = array.Clone() as T[]
			};
		}

		public void CopyInto(TwoDimensionalArray<T> other, Vector2Int dst_position, Vector2Int src_position)
		{
			CopyInto(other, dst_position, src_position, new Vector2Int(Width, Height));
		}

		public void CopyInto(TwoDimensionalArray<T> other, Vector2Int dst_position, Vector2Int src_position, Vector2Int size)
		{
			if (dst_position.x < 0 ||
				dst_position.y < 0 ||
				src_position.x < 0 ||
				src_position.y < 0 ||
				size.x < 1 ||
				size.y < 1)
				throw new System.ArgumentException("positions can't be negative, size must be positive");

			for (
				int this_y = src_position.y, that_y = dst_position.y;
				this_y < height && this_y < src_position.y + size.y && that_y < other.Height;
				this_y++, that_y++)
			{
				for (
				int this_x = src_position.x, that_x = dst_position.x;
				this_x < width && this_x < src_position.x + size.x && that_x < other.Width;
				this_x++, that_x++)
				{
					other[that_x, that_y] = this[this_x, this_y];
				}
			}
		}

		public void CopyInto(T defaultValue, TwoDimensionalArray<T> other, Vector2Int dst_position, Vector2Int src_position)
		{
			CopyInto(defaultValue, other, dst_position, src_position, new Vector2Int(Width, Height));
		}

		public void CopyInto(T defaultValue, TwoDimensionalArray<T> other, Vector2Int dst_position, Vector2Int src_position, Vector2Int size)
		{
			if (dst_position.x < 0 ||
				dst_position.y < 0 ||
				src_position.x < 0 ||
				src_position.y < 0 ||
				size.x < 1 ||
				size.y < 1)
				throw new System.ArgumentException("positions can't be negative, size must be positive");

			for (int y = 0; y < other.Height; ++y)
			{
				for (int x = 0; x < other.Width; ++x)
				{
					var v = new Vector2Int(x, y) - dst_position + src_position;
					if (
						x < dst_position.x ||
						x >= dst_position.x + size.x ||
						y < dst_position.y ||
						y >= dst_position.y + size.y ||
						v.x < 0 ||
						v.x >= width ||
						v.y < 0 ||
						v.y >= height
						)
					{
						other[x, y] = defaultValue;
					}
					else
					{
						other[x, y] = this[v];
					}
				}
			}
		}

		public override string ToString()
		{
			var s = "";
			for (int y = 0; y < height; ++y)
			{
				for (int x = 0; x < width; ++x)
				{
					s += $"{this[x, y]}, ";
				}
				s += "\n";
			}
			return s;
		}

		public T this[int x, int y]
		{
			get => array[y * Width + x];
			set => array[y * Width + x] = value;
		}

		public T this[Vector2Int v]
		{
			get => this[v.x, v.y];
			set => this[v.x, v.y] = value;
		}
	}
}
