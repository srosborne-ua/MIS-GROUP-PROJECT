using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Staff accounts, managed from the Staff Portal's Employees tab. Any signed-in employee
// can see the list; only managers can change accounts.
[ApiController]
[Route("api/employees")]
public class EmployeesController(EmployeeStore employees) : ControllerBase
{
    // GET api/employees
    [HttpGet]
    public Task<List<Employee>> GetEmployees() => employees.ListAsync();

    // POST api/employees
    [HttpPost]
    [Authorize(Roles = EmployeeStore.Manager)]
    public Task<Employee> CreateEmployee(CreateEmployeeRequest input) => employees.CreateAsync(input);

    // PUT api/employees/3/active
    [HttpPut("{id:int}/active")]
    [Authorize(Roles = EmployeeStore.Manager)]
    public Task<Employee> SetActive(int id, EmployeeActiveRequest input) =>
        employees.SetActiveAsync(id, input.Active, StaffSession.EmployeeId(User));

    // PUT api/employees/3/role
    [HttpPut("{id:int}/role")]
    [Authorize(Roles = EmployeeStore.Manager)]
    public Task<Employee> SetRole(int id, EmployeeRoleRequest input) =>
        employees.SetRoleAsync(id, input.Role, StaffSession.EmployeeId(User));

    // PUT api/employees/3/password
    [HttpPut("{id:int}/password")]
    [Authorize(Roles = EmployeeStore.Manager)]
    public async Task<IActionResult> SetPassword(int id, SetPasswordRequest input)
    {
        await employees.SetPasswordAsync(id, input.Password, StaffSession.EmployeeId(User));
        return NoContent();
    }
}
