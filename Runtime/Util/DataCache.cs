using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Barmetler
{
	public class DataCache<T>
	{
		T data;
		bool valid = false;

		public void SetData(T data)
		{
			this.data = data;
			valid = true;
		}

		public T GetData()
		{
			if (!IsValid()) throw new System.Exception("Cache is invalid");
			return data;
		}

		public void Invalidate()
		{
			valid = false;
		}

		public bool IsValid()
		{
			return valid;
		}
	}
}