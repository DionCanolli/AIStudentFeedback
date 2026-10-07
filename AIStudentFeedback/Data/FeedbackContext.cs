using AIFeedbackSystem.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace AIFeedbackSystem.Data;

public class FeedbackContext : DbContext
{
    public FeedbackContext(DbContextOptions<FeedbackContext> options) : base(options) { }

    public DbSet<StudentFeedback> Feedbacks { get; set; }
}