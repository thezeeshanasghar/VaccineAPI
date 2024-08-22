using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;
using AutoMapper;
using VaccineAPI.ModelDTO;
namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InvoiceController : ControllerBase
    {
        private readonly Context _context;

        public InvoiceController(Context context)
        {
            _context = context;
        }
private async Task<string> GenerateUniqueInvoiceId(int length)
{
    string invoiceId;
    bool isUnique;

    do
    {
        invoiceId = GenerateRandomInvoiceId(length);
        isUnique = !await _context.Invoices.AnyAsync(i => i.InvoiceId == invoiceId);
    } while (!isUnique);

    return invoiceId;
}

private string GenerateRandomInvoiceId(int length)
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    var random = new Random();
    return new string(Enumerable.Repeat(chars, length)
        .Select(s => s[random.Next(s.Length)]).ToArray());
}


        // POST: api/Invoice
   [HttpPost]
public async Task<ActionResult<Invoice>> CreateInvoice(InvoiceDTO dto)
{
    // Check if the ChildId exists
    var childExists = await _context.Childs.AnyAsync(c => c.Id == dto.ChildId);
    if (!childExists)
    {
        return BadRequest("Invalid ChildId. The child does not exist.");
    }

    // Generate a unique 25-character alphanumeric InvoiceId
    string generatedInvoiceId = await GenerateUniqueInvoiceId(25);

    // Map the DTO to the Invoice entity
    var invoice = new Invoice
    {
        InvoiceId = generatedInvoiceId,  // Use the unique generated ID
        Amount = dto.Amount,
        ChildId = dto.ChildId
    };

    // Add the invoice to the context and save changes
    _context.Invoices.Add(invoice);
    await _context.SaveChangesAsync();

    // Return the created invoice
    return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
}


        // GET: api/Invoice/{id}
     [HttpGet("{id}")]
public async Task<ActionResult<Invoice>> GetInvoice(long id)
{
    try
    {
        var invoice = await _context.Invoices.FindAsync(id);

        if (invoice == null)
        {
            return NotFound();
        }

        return invoice;
    }
    catch (InvalidCastException ex)
    {
        // Log the error
        // _logger.LogError(ex, "Invalid cast when retrieving invoice with ID {InvoiceId}", id);
        return StatusCode(500, "An error occurred while processing your request.");
    }
}

    }
}