namespace Barmetler
{
	public class DataCache<T>
	{
		private T data;
		private bool valid = false;

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