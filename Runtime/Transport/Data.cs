using System;
using UnityEngine;
using Transport;

namespace Transport
{
    public abstract class BaseData<T>: IDisposable where T: unmanaged, IPackage<T>
    {
        private bool is_create = false;
        protected T package;

        public void InsertPackage(T package)
        {
            Dispose(true);
            this.package = package;
            is_create = true;
        }

        public T ExtractPackage()
        {
            if (!is_create)
                return default;
            if (package.IsReleasable)
            {
                T tmp_package = package;
                package = default;
                is_create = false;
                return tmp_package;
            }
            else
                return package;
        }

        ~BaseData()
        {
            Dispose(false);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (is_create)
            {
                if (disposing)
                {
                    // free managed resources
                }
                // free unmanaged resources
                package.Release();
                is_create = false;
            }
        }
    }
};


