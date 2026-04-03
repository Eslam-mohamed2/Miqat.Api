using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Miqat.Application.Common
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }

        public static ApiResponse<T> Ok(T data, string? message = null) => new()
        {
            Success = true,
            Data = data,
            Message = message
        };

        public static ApiResponse<T> Fail(string message, List<string>? errors = null) => new()
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}
