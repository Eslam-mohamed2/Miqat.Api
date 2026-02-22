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
        public bool IsSuccess { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
        public List<string> Errors { get; set; }
        // Success response constructor
        public ApiResponse(T data, string message = null!)
        {
            IsSuccess = true;
            Data = data;
            Message = message;
        }
        // Failure response constructor
        public ApiResponse(string message, List<string> errors = null!)
        {
            IsSuccess = false;
            Message = message;
            Errors = errors;
        }
    }
}
